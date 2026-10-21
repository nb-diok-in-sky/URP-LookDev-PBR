// Look-Dev 快速输出：选中变量 → 在片元着色器最后一个 return 前插入 return float4(变量, 1);
const vscode = require('vscode');

const MARK = '// LD临时';

// 去掉注释和字符串，换成等长空格，方便找括号和 return 时不被注释干扰（位置不变）
function blankComments(text) {
    return text
        .replace(/\/\*[\s\S]*?\*\//g, m => m.replace(/[^\n]/g, ' '))
        .replace(/\/\/[^\n]*/g, m => ' '.repeat(m.length))
        .replace(/"[^"\n]*"/g, m => ' '.repeat(m.length));
}

// 找片元函数的函数体范围 [开始大括号, 结束大括号]
function findFragmentBody(text) {
    const clean = blankComments(text);
    const pragma = /#pragma\s+fragment\s+(\w+)/.exec(clean);
    if (!pragma) return { error: '没找到 #pragma fragment' };
    const name = pragma[1];
    // 找函数定义：名字后面跟参数列表，再跟 { （中间可以有 : SV_Target）
    const defRe = new RegExp('\\b' + name + '\\s*\\(', 'g');
    let m;
    while ((m = defRe.exec(clean))) {
        // 跳过参数括号
        let i = m.index + m[0].length, depth = 1;
        while (i < clean.length && depth > 0) {
            if (clean[i] === '(') depth++;
            else if (clean[i] === ')') depth--;
            i++;
        }
        // 参数后面到 { 之间只能有空白和 : 语义
        const rest = /^\s*(:\s*\w+\s*)?\{/.exec(clean.slice(i));
        if (!rest) continue;
        const open = i + rest[0].length - 1;
        let j = open + 1; depth = 1;
        while (j < clean.length && depth > 0) {
            if (clean[j] === '{') depth++;
            else if (clean[j] === '}') depth--;
            j++;
        }
        if (depth !== 0) return { error: '片元函数 ' + name + ' 的大括号没配对上' };
        return { name, open, close: j - 1, clean };
    }
    return { error: '没找到片元函数 ' + name + ' 的定义' };
}

// 在文件里猜变量的维度：float / float2 / float3 / float4（half 同理）
function guessDim(text, expr) {
    const ident = (expr.match(/(\w+)\s*$/) || [])[1];
    if (!ident) return null;
    const re = new RegExp('\\b(?:float|half|real|min16float)([234]?)\\s+' + ident + '\\b');
    const m = re.exec(blankComments(text));
    if (!m) return null;
    return m[1] === '' ? 1 : Number(m[1]);
}

// 像方向向量的名字：以 Dir / Direction 结尾，或者 normal、tangent 开头的，或者 R、h
// （directColor 这种名字里含 dir 但不是方向的，不算）
function isDirectionName(ident) {
    return /^(?:\w*(?:dir|direction)|normal\w*|\w*tangent\w*|R|h|H)$/i.test(ident);
}

function suffixFor(dim) {
    if (dim === 1) return '.xxx';
    if (dim === 2) return '.xyy';
    return '.rgb';
}

// 一个单独的名字（不带 . 的），在片元函数里有没有声明过
function declaredInBody(bodyText, ident) {
    const re = new RegExp('\\b(?:float|half|real|min16float|int|uint|bool)[234]?(?:x[234])?\\s+' + ident + '\\b'
        + '|\\b[A-Z]\\w*\\s+' + ident + '\\s*[;=]');
    return re.test(bodyText);
}

async function insert(mode) {
    const editor = vscode.window.activeTextEditor;
    if (!editor) return;
    const doc = editor.document;
    let sel = editor.selection;
    if (sel.isEmpty) {
        const range = doc.getWordRangeAtPosition(sel.active, /[\w.]+/);
        if (!range) { vscode.window.showWarningMessage('先选中一个变量或表达式'); return; }
        sel = new vscode.Selection(range.start, range.end);
    }
    const expr = doc.getText(sel).trim();
    if (!expr) return;

    const text = doc.getText();
    const body = findFragmentBody(text);
    if (body.error) { vscode.window.showWarningMessage('Look-Dev：' + body.error); return; }

    // 片元函数里最后一个 return
    const inner = body.clean.slice(body.open, body.close);
    let last = -1, r;
    const retRe = /\breturn\b/g;
    while ((r = retRe.exec(inner))) last = r.index;
    if (last < 0) { vscode.window.showWarningMessage('片元函数里没有 return'); return; }
    const retPos = doc.positionAt(body.open + last);
    const retLine = doc.lineAt(retPos.line);
    const indent = retLine.text.slice(0, retLine.firstNonWhitespaceCharacterIndex);

    // 选的是单独一个名字，但片元函数里没有这个变量：多半选到了别的函数的参数或者结构体字段
    const fragSignature = body.clean.slice(0, body.open).split(/\n/).slice(-3).join('\n');
    if (/^\w+$/.test(expr) && !declaredInBody(inner, expr) && !new RegExp('\\b' + expr + '\\b').test(fragSignature)) {
        vscode.window.showWarningMessage('Look-Dev：片元函数里没有叫 ' + expr + ' 的变量（可能是别的函数的参数或结构体字段），'
            + '请选片元函数里实际用到的写法，比如 xxxContext.' + expr);
        return;
    }

    const needParen = /[^\w.]/.test(expr);
    const wrapped = needParen ? '(' + expr + ')' : expr;
    const at = new vscode.Position(retPos.line, 0);

    // 正负显示：正数绿、负数红（只看第一个分量）
    if (mode === 'signed') {
        const v = wrapped + '.x';
        const line = indent + 'return float4(max(-' + v + ', 0), max(' + v + ', 0), 0, 1); ' + MARK + '\n';
        await editor.edit(b => b.insert(at, line));
        const p = new vscode.Position(retPos.line, indent.length);
        editor.selection = new vscode.Selection(p, p);
        editor.revealRange(new vscode.Range(p, p), vscode.TextEditorRevealType.InCenterIfOutsideViewport);
        return;
    }

    // 决定怎么补成 float4
    let suffix;
    const dim = guessDim(text, expr);
    if (dim) {
        suffix = suffixFor(dim);
    } else {
        const pick = await vscode.window.showQuickPick([
            { label: '.xxx', description: '一个数（float），显示成灰度' },
            { label: '.rgb', description: '颜色或 float3 / float4' },
            { label: '.rgb * 0.5 + 0.5', description: '方向向量，从 -1~1 挪到 0~1' },
            { label: '.xyy', description: 'float2' }
        ], { placeHolder: '没认出 ' + expr + ' 的维度，选一种显示方式' });
        if (!pick) return;
        suffix = pick.label;
    }
    // 名字里像方向的，默认帮你挪到 0~1
    const lastIdent = (expr.match(/(\w+)\s*$/) || [])[1] || '';
    if (dim && dim >= 3 && isDirectionName(lastIdent)) suffix = '.rgb * 0.5 + 0.5';

    const line = indent + 'return float4(' + wrapped + suffix + ', 1); ' + MARK + '\n';
    await editor.edit(b => b.insert(at, line));
    const pos = new vscode.Position(retPos.line, indent.length);
    editor.selection = new vscode.Selection(pos, pos);
    editor.revealRange(new vscode.Range(pos, pos), vscode.TextEditorRevealType.InCenterIfOutsideViewport);
}

async function removeAll() {
    const editor = vscode.window.activeTextEditor;
    if (!editor) return;
    const doc = editor.document;
    const lines = [];
    for (let i = 0; i < doc.lineCount; i++) if (doc.lineAt(i).text.includes(MARK)) lines.push(i);
    if (!lines.length) { vscode.window.setStatusBarMessage('Look-Dev：没有临时输出', 2000); return; }
    await editor.edit(b => { for (const i of lines) b.delete(doc.lineAt(i).rangeIncludingLineBreak); });
    vscode.window.setStatusBarMessage('Look-Dev：删掉了 ' + lines.length + ' 行临时输出', 2000);
}

function activate(context) {
    context.subscriptions.push(
        vscode.commands.registerCommand('ldQuickReturn.insert', () => insert('auto')),
        vscode.commands.registerCommand('ldQuickReturn.insertSigned', () => insert('signed')),
        vscode.commands.registerCommand('ldQuickReturn.removeAll', removeAll)
    );
}
function deactivate() {}
module.exports = { activate, deactivate, _test: { findFragmentBody, guessDim, suffixFor, blankComments, isDirectionName, declaredInBody } };
