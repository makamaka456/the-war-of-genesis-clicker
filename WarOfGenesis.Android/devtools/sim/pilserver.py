# 시험기의 글자 · 그림 도우미 — 시험기(C#)가 표준 입출력으로 부른다. 안드로이드의 Paint · BitmapFactory 자리를 리눅스에서 PIL 로 메운다.
# 한 줄 명령(JSON) → 한 줄 답(JSON). 그림 · 글 점은 임시 파일(RGBA 날바이트)로 주고받는다.
#   {"op":"measure","text":..,"size":..,"bold":..,"file":..}            → {"w":..,"h":..}
#   {"op":"draw","text":..,"size":..,"bold":..,"file":..,"w":..,"h":..,"x":..,"y":..,"argb":..,"out":경로} → {"ok":1}   (경로에 w*h*4 RGBA)
#   {"op":"decode","path":..,"out":경로}                                   → {"w":..,"h":..}
import json, sys
from PIL import Image, ImageDraw, ImageFont

FALLBACK = "/usr/share/fonts/truetype/wqy/wqy-zenhei.ttc"
fonts = {}


def font(size, bold, file):
    key = (round(size * 4), bool(bold), file)
    if key not in fonts:
        path = file or FALLBACK
        try:
            fonts[key] = ImageFont.truetype(path, max(1, round(size)))
        except OSError:
            fonts[key] = ImageFont.truetype(FALLBACK, max(1, round(size)))
    return fonts[key]


for line in sys.stdin:
    req = json.loads(line)
    op = req["op"]
    try:
        if op == "measure":
            f = font(req["size"], req["bold"], req.get("file"))
            l, t, r, b = f.getbbox(req["text"] or " ")
            asc, desc = f.getmetrics()
            res = {"w": f.getlength(req["text"]), "h": asc + desc}
        elif op == "draw":
            f = font(req["size"], req["bold"], req.get("file"))
            w, h = req["w"], req["h"]
            img = Image.frombytes("RGBA", (w, h), open(req["out"], "rb").read())
            layer = Image.new("RGBA", (w, h), (0, 0, 0, 0))
            argb = req["argb"]
            color = ((argb >> 16) & 255, (argb >> 8) & 255, argb & 255, (argb >> 24) & 255)
            d = ImageDraw.Draw(layer)
            d.text((req["x"], req["y"]), req["text"], font=f, fill=color, stroke_width=1 if req["bold"] else 0, stroke_fill=color)
            img = Image.alpha_composite(img, layer)
            open(req["out"], "wb").write(img.tobytes())
            res = {"ok": 1}
        elif op == "decode":
            img = Image.open(req["path"]).convert("RGBA")
            open(req["out"], "wb").write(img.tobytes())
            res = {"w": img.width, "h": img.height}
        else:
            res = {"error": "op"}
    except Exception as ex:  # 시험기가 알아서 처리한다
        res = {"error": str(ex)}
    sys.stdout.write(json.dumps(res) + "\n")
    sys.stdout.flush()
