# 用法：python Tools/render_code_diff.py（需要 pygments、Pillow）
# 把 git 里每次 shader 改动渲染成 VS 深色风格的代码图：删掉的行标红、加上的行标绿，带行号
import os, re, subprocess, sys
from PIL import Image, ImageDraw, ImageFont
from pygments.lexers import get_lexer_by_name
from pygments.token import Token

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))   # Tools 的上一级就是工程根目录
PATH = "Assets/LookDev/MyAssets/BRDF/URP_Shader.shader"
OUT = os.path.join(REPO, "Screenshots", "代码改动")
FONT = ImageFont.truetype(r"C:\Windows\Fonts\simsun.ttc", 20, index=1)   # 新宋体，和 VS Code 里一样
TITLE_FONT = ImageFont.truetype(r"C:\Windows\Fonts\msyh.ttc", 22)
SKIP_PREFIX = "整理格式"    # 提交说明以这个开头的（只改格式、不改逻辑）不画
MAX_LINES = 70              # 一张图最多多少行，超过就分图

# Visual Studio 深色主题的颜色
BG = (30, 30, 30); FG = (220, 220, 220); GUTTER = (43, 145, 175)
DEL_BG = (75, 24, 24); ADD_BG = (30, 58, 30); DEL_MARK = (230, 100, 100); ADD_MARK = (110, 200, 110)
HUNK_BG = (45, 45, 48); TITLE_BG = (0, 122, 204)
COLORS = [
    (Token.Comment.Preproc, (155, 155, 155)),     # 预处理指令要排在注释前面，不然会被当成注释画成绿色
    (Token.Comment, (87, 166, 74)),
    (Token.Keyword.Type, (86, 156, 214)),
    (Token.Keyword, (86, 156, 214)),
    (Token.Literal.String, (214, 157, 133)),
    (Token.Literal.Number, (181, 206, 168)),
    (Token.Name.Builtin, (78, 201, 176)),
]
LEXER = get_lexer_by_name("hlsl")
CW = int(FONT.getlength("M"))       # 等宽字体里一个半角字符的宽度
LH = 26                             # 行高


def color_of(ttype):
    for t, c in COLORS:
        if ttype in t:
            return c
    return FG


def text_width(s):
    return int(FONT.getlength(s))


def git(*args):
    return subprocess.run(["git", "-C", REPO, *args], capture_output=True, text=True, encoding="utf-8").stdout


def parse_diff(h):
    # -w 忽略纯空白改动；-U3 上下各留 3 行上下文
    diff = git("show", "-w", "-U3", "--format=", h, "--", PATH)
    hunks = []
    for line in diff.splitlines():
        m = re.match(r"@@ -(\d+)(?:,\d+)? \+(\d+)(?:,\d+)? @@", line)
        if m:
            hunks.append({"old": int(m.group(1)), "new": int(m.group(2)), "lines": []})
        elif hunks and line[:1] in (" ", "+", "-") and not line.startswith(("+++", "---")):
            hunks[-1]["lines"].append((line[0], line[1:].replace("\t", "    ")))
    return hunks


def draw_line(draw, y, x0, kind, oldno, newno, code, width):
    if kind == "-":
        draw.rectangle([0, y, width, y + LH], fill=DEL_BG)
    elif kind == "+":
        draw.rectangle([0, y, width, y + LH], fill=ADD_BG)
    draw.text((8, y + 3), f"{oldno:>4}" if oldno else "    ", font=FONT, fill=GUTTER)
    draw.text((8 + CW * 5, y + 3), f"{newno:>4}" if newno else "    ", font=FONT, fill=GUTTER)
    mark = {"-": "-", "+": "+"}.get(kind, " ")
    draw.text((8 + CW * 10, y + 3), mark, font=FONT, fill=DEL_MARK if kind == "-" else ADD_MARK)
    x = x0
    for ttype, value in LEXER.get_tokens(code):
        value = value.rstrip("\n")
        if not value:
            continue
        draw.text((x, y + 3), value, font=FONT, fill=color_of(ttype))
        x += text_width(value)


def render(rows, title, path):
    x0 = 8 + CW * 12
    width = max([x0 + text_width(r[3]) + 30 for r in rows if r[0] != "@"] + [900, int(TITLE_FONT.getlength(title)) + 40])
    height = 44 + LH * len(rows) + 10
    img = Image.new("RGB", (width, height), BG)
    d = ImageDraw.Draw(img)
    d.rectangle([0, 0, width, 40], fill=TITLE_BG)
    d.text((14, 7), title, font=TITLE_FONT, fill=(255, 255, 255))
    y = 44
    for kind, o, n, code in rows:
        if kind == "@":
            d.rectangle([0, y, width, y + LH], fill=HUNK_BG)
            d.text((8, y + 3), code, font=FONT, fill=(150, 150, 150))
        else:
            draw_line(d, y, x0, kind, o, n, code, width)
        y += LH
    img.save(path)


def main():
    os.makedirs(OUT, exist_ok=True)
    log = git("log", "--reverse", "--format=%h\t%s", "--", PATH).strip().splitlines()
    made = []
    for idx, line in enumerate(log, 1):
        h, msg = line.split("\t", 1)
        if idx == 1 or msg.startswith(SKIP_PREFIX):
            continue
        rows = []
        for hk in parse_diff(h):
            rows.append(("@", 0, 0, f"…… 第 {hk['new']} 行附近 ……"))
            o, n = hk["old"], hk["new"]
            for kind, code in hk["lines"]:
                if kind == " ":
                    rows.append((" ", o, n, code)); o += 1; n += 1
                elif kind == "-":
                    rows.append(("-", o, 0, code)); o += 1
                else:
                    rows.append(("+", 0, n, code)); n += 1
        if not any(r[0] in "+-" for r in rows):
            continue
        # 按 hunk 边界切成每张不超过 MAX_LINES 行
        pages, cur = [], []
        for r in rows:
            if r[0] == "@" and cur and len(cur) > MAX_LINES * 0.6:
                pages.append(cur); cur = []
            cur.append(r)
            if len(cur) >= MAX_LINES:
                pages.append(cur); cur = []
        if cur:
            pages.append(cur)
        short = msg if len(msg) <= 46 else msg[:45] + "…"
        for p, page in enumerate(pages, 1):
            suffix = f"_{p}" if len(pages) > 1 else ""
            name = f"{idx:02d}_{h}_代码{suffix}.png"
            title = f"第 {idx:02d} 版   提交 {h}" + (f"   （{p}/{len(pages)}）" if len(pages) > 1 else "")
            render(page, title, os.path.join(OUT, name))
            made.append(name)
    print(len(made), "张")
    print("\n".join(made))


main()
