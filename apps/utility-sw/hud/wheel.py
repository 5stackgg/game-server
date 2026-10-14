"""Generates nade2_wheel.xml and nadewheel2.css for the radial menu.

Both files are output: edit this and run it, never them.

    python3 wheel.py                  writes into panorama/ beside this file
    python3 wheel.py <dir> [geom.svg] writes into <dir>, and a picture of the
                                      geometry to look at before it goes in game

Everything is in pixels at 1080p in a square box with the crosshair at its centre.
Wedge k is centred on k * 45 degrees, clockwise from the top. The hit counts it
prints are HudSlots.WheelHits, WheelQuickHits and WheelWideHits.
"""
import math, sys, os

OUT = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(os.path.abspath(__file__)), "panorama")
SVG = sys.argv[2] if len(sys.argv) > 2 else None
BOX = 700
C = BOX / 2
R_IN, R_OUT = 112, 268
RIM_IN, RIM_OUT = 257, 265
# the outer ring: two small quick actions over each wedge, held off the wedges
# by a clear gap so the two rings read as two things
Q_IN, Q_OUT = 280, 318
Q_STEP = 360 / 16
Q_LABEL_R = 299
Q_LABEL_W, Q_LABEL_H = 112, 20
GAP = 2.4
SLOTS = 8
STEP = 360 / SLOTS
LABEL_R = 191
LABEL_W, LABEL_H = 150, 44
HUB = 204

def frac(r):
    return round(r / BOX, 4)

def ring(inner, outer, color):
    clear = color[:7] + "00"
    return (
        "gradient( radial, 50% 50%, 0% 0%, 100% 100%, "
        f"from( {clear} ), color-stop( {frac(inner)}, {clear} ), color-stop( {frac(inner)}, {color} ), "
        f"color-stop( {frac(outer)}, {color} ), color-stop( {frac(outer)}, {clear} ), to( {clear} ) )"
    )

# a ring that is one colour to `edge`, then another to the outside
def ring2(inner, edge, outer, color, rim):
    clear = color[:7] + "00"
    return (
        "gradient( radial, 50% 50%, 0% 0%, 100% 100%, "
        f"from( {clear} ), color-stop( {frac(inner)}, {clear} ), color-stop( {frac(inner)}, {color} ), "
        f"color-stop( {frac(edge)}, {color} ), color-stop( {frac(edge)}, {rim} ), "
        f"color-stop( {frac(outer)}, {rim} ), color-stop( {frac(outer)}, {clear} ), to( {clear} ) )"
    )

# hit rectangles, as (x0, y0, x1, y1) offsets from the centre with y up
BAND = 24
Q_BAND = 13
REACH = 12

def in_sector(x, y, centre, span, r_in, r_out, slack=0.4):
    r = math.hypot(x, y)
    if r < r_in or r > r_out:
        return False
    angle = math.degrees(math.atan2(x, y)) % 360
    delta = (angle - centre + 180) % 360 - 180
    return abs(delta) <= span / 2 - GAP / 2 - slack

def cover(centre, span, r_in, r_out, band, least):
    """Horizontal bands: in each, the widest run of x that is inside the sector at every height of the band."""
    out = []
    limit = int(r_out) + 2
    y = -limit
    while y < limit:
        top = y + band
        best = None
        run = None
        for x in range(-limit, limit + 1):
            ok = all(in_sector(x, yy, centre, span, r_in, r_out) for yy in (y, y + band / 3, y + 2 * band / 3, top))
            if ok:
                run = (run[0], x) if run else (x, x)
            else:
                if run and (best is None or run[1] - run[0] > best[1] - best[0]):
                    best = run
                run = None
        if run and (best is None or run[1] - run[0] > best[1] - best[0]):
            best = run
        if best and best[1] - best[0] >= least:
            out.append((best[0], y, best[1], top))
        y = top
    return out

def hits(slot):
    return cover(slot * STEP, STEP, R_IN, R_OUT, BAND, 14)

def quick_centre(index):
    return (index // 2) * STEP + (-Q_STEP / 2 if index % 2 == 0 else Q_STEP / 2)

def quick_hits(index):
    return cover(quick_centre(index), Q_STEP, Q_IN, Q_OUT + REACH, Q_BAND, 10)

# a wedge with only one quick action gives it the whole width of the wedge
def wide_hits(slot):
    return cover(slot * STEP, STEP, Q_IN, Q_OUT + REACH, Q_BAND, 10)

def inside(slot, x, y):
    r = math.hypot(x, y)
    if r < R_IN or r > R_OUT:
        return False
    angle = math.degrees(math.atan2(x, y)) % 360
    delta = (angle - slot * STEP + 180) % 360 - 180
    return abs(delta) <= STEP / 2 - GAP / 2

# every rectangle has to stay out of every other sector, and should cover most of its own
sectors = [("w", slot, slot * STEP, STEP, R_IN, R_OUT, hits(slot)) for slot in range(SLOTS)]
sectors += [("q", index, quick_centre(index), Q_STEP, Q_IN, Q_OUT, quick_hits(index)) for index in range(16)]
sectors += [("x", slot, slot * STEP, STEP, Q_IN, Q_OUT, wide_hits(slot)) for slot in range(SLOTS)]
for kind, index, centre, span, r_in, r_out, rects in sectors:
    covered = total = stray = 0
    for xi in range(-340, 341, 3):
        for yi in range(-340, 341, 3):
            mine = in_sector(xi, yi, centre, span, r_in, r_out, 0)
            hit = any(r[0] <= xi <= r[2] and r[1] <= yi <= r[3] for r in rects)
            if mine:
                total += 1
                covered += hit
            if hit and any(
                in_sector(xi, yi, c2, s2, ri2, ro2, 0)
                for k2, i2, c2, s2, ri2, ro2, _ in sectors
                # a wide segment and the two halves under it are never shown together
                if (k2, i2) != (kind, index) and {kind, k2} != {"q", "x"}
            ):
                stray += 1
    assert stray == 0, (kind, index, stray)
    print(f"{kind}{index}: {len(rects)} hit rects, covers {covered / total:.0%}")

def wide_clip(slot):
    start = (slot * STEP - STEP / 2 + GAP / 2) % 360
    return f"clip: radial( 50% 50%, {start:g}deg, {STEP - GAP:g}deg );"

# Turned to lie along the ring at its angle, like text set on the circle. The
# three wedges along the bottom turn it the other way round, so it reads left to
# right there and not upside down. Decided by wedge, not by label: the two
# halves of a side wedge have to read the same way.
def tangent(degrees):
    degrees %= 360
    if STEP * 2.5 < degrees < STEP * 5.5:
        degrees -= 180
    elif degrees > 180:
        degrees -= 360
    return f"transform: rotateZ( {degrees:g}deg );"

def quick_clip(index):
    start = (quick_centre(index) - Q_STEP / 2 + GAP / 2) % 360
    return f"clip: radial( 50% 50%, {start:g}deg, {Q_STEP - GAP:g}deg );"

def clip(slot):
    start = (slot * STEP - STEP / 2 + GAP / 2) % 360
    return f"clip: radial( 50% 50%, {start:g}deg, {STEP - GAP:g}deg );"

xml = []
css = []

xml.append('''<root>
	<styles>
		<include src="s2r://panorama/styles/custom_game/nadewheel2.vcss_c" />
	</styles>
	<Panel class="nw-screen" hittest="false">
		<Panel id="NadeWheel" class="nw-wheel" hittest="true">
			<Button id="close" class="nw-away" />
			<Panel class="nw-box" hittest="false">
				<Panel class="nw-shade" hittest="false" />
				<Panel class="nw-floor" hittest="false" />''')

for slot in range(SLOTS):
    n = slot + 1
    xml.append(f'\t\t\t\t<Panel id="r{n}" class="nw-wedge nw-k{slot}" hittest="false">')
    xml.append('\t\t\t\t\t<Panel class="nw-fill" hittest="false" />')
    xml.append('\t\t\t\t\t<Panel class="nw-rim" hittest="false" />')
    for index, _ in enumerate(hits(slot)):
        xml.append(f'\t\t\t\t\t<Button id="r{n}h{index + 1}" class="nw-hit"><Panel class="nw-lit" hittest="false" /></Button>')
    xml.append(f'\t\t\t\t\t<Panel class="nw-text" hittest="false"><Label id="w{n}" class="nw-label" text="{{s:w{n}}}" hittest="false" /><Label id="s{n}" class="nw-sub" text="{{s:s{n}}}" hittest="false" /></Panel>')
    xml.append('\t\t\t\t</Panel>')

for index in range(16):
    n = index + 1
    xml.append(f'\t\t\t\t<Panel id="o{n}" class="nw-quick nw-q{index}" hittest="false">')
    xml.append('\t\t\t\t\t<Panel class="nw-qfill" hittest="false" />')
    xml.append('\t\t\t\t\t<Panel class="nw-qedge" hittest="false" />')
    for hit, _ in enumerate(quick_hits(index)):
        xml.append(f'\t\t\t\t\t<Button id="o{n}h{hit + 1}" class="nw-hit"><Panel class="nw-qlit" hittest="false" /></Button>')
    xml.append(f'\t\t\t\t\t<Label id="q{n}" class="nw-qlabel" text="{{s:q{n}}}" hittest="false" />')
    xml.append('\t\t\t\t</Panel>')

for slot in range(SLOTS):
    n = slot + 1
    xml.append(f'\t\t\t\t<Panel id="x{n}" class="nw-quick nw-x{slot}" hittest="false">')
    xml.append('\t\t\t\t\t<Panel class="nw-qfill" hittest="false" />')
    xml.append('\t\t\t\t\t<Panel class="nw-qedge" hittest="false" />')
    for hit, _ in enumerate(wide_hits(slot)):
        xml.append(f'\t\t\t\t\t<Button id="x{n}h{hit + 1}" class="nw-hit"><Panel class="nw-qlit" hittest="false" /></Button>')
    xml.append(f'\t\t\t\t\t<Label id="xl{n}" class="nw-qlabel" text="{{s:xl{n}}}" hittest="false" />')
    xml.append('\t\t\t\t</Panel>')

xml.append('''				<Button id="hub" class="nw-hub"><Label id="hubt" class="nw-hub-title" text="{s:hubt}" hittest="false" /><Label id="hubs" class="nw-hub-sub" text="{s:hubs}" hittest="false" /></Button>
			</Panel>
		</Panel>
	</Panel>
</root>
''')

css.append(f'''/* nadewheel2.vcss -- the radial menu, and nothing else.

   Its own stylesheet on purpose: it uses things no other layout here does (a
   radial clip, radial gradients), and a stylesheet the game rejects takes
   every layout that includes it down with it. Kept apart, the worst a mistake
   in here can do is break the wheel.

   The wedges are built the way the game builds its own radio wheel: a panel
   the size of the whole wheel, painted with a ring by a radial gradient, and
   cut down to one slice by a radial clip. Nothing is rotated.

   The game only hit-tests rectangles, so each wedge carries a few invisible
   buttons that together cover most of it and none of its neighbours. Each
   button holds its own copy of the lit wedge and shows it while hovered, so
   hovering any of them lights the whole slice.

   Every number below is generated: the box is {BOX}px with the crosshair at its
   centre, the ring runs from {R_IN}px to {R_OUT}px, and slot k is centred on
   k x {STEP:g} degrees clockwise from the top. */

.nw-screen
{{
	width: 100%;
	height: 100%;
}}

.nw-wheel
{{
	width: 100%;
	height: 100%;
	visibility: collapse;
	opacity: 0;
	transition-property: opacity;
	transition-duration: 0.08s;
}}

.nw-wheel.shown
{{
	visibility: visible;
	opacity: 1;
}}

/* The whole screen, under the wheel: a click that lands on nothing closes it,
   the way clicking off any menu does. */
.nw-away
{{
	width: 100%;
	height: 100%;
}}

.nw-box
{{
	width: {BOX}px;
	height: {BOX}px;
	horizontal-align: center;
	vertical-align: center;
	flow-children: none;
	transform: scale3d( 0.94, 0.94, 1 );
	transition-property: transform;
	transition-duration: 0.16s;
	transition-timing-function: ease-out;
}}

/* Opens from a touch smaller, the way the game's own radio wheel arrives. */
.nw-wheel.shown .nw-box
{{
	transform: none;
}}

/* Darkens the world behind the wheel so it reads over sand and sky alike. */
.nw-shade
{{
	width: 100%;
	height: 100%;
	background-color: gradient( radial, 50% 50%, 0% 0%, 100% 100%, from( #00000070 ), color-stop( {frac(R_OUT + 6)}, #00000070 ), color-stop( {frac(R_OUT + 30)}, #00000000 ), to( #00000000 ) );
}}

/* The whole ring, faintly, so a wheel with fewer than eight things on it
   still looks like a wheel. */
.nw-floor
{{
	width: 100%;
	height: 100%;
	background-color: {ring(R_IN, R_OUT, "#09090b55")};
}}

.nw-wedge
{{
	width: 100%;
	height: 100%;
	flow-children: none;
}}

.nw-wedge.blank
{{
	visibility: collapse;
}}

/* Nothing to do it to: no throw to repeat, no second lineup here. */
.nw-wedge.disabled
{{
	opacity: 0.36;
}}

.nw-fill
{{
	width: 100%;
	height: 100%;
	background-color: {ring(R_IN, R_OUT, "#09090be0")};
}}

/* The choice a filter is set to. */
.nw-wedge.pick .nw-fill
{{
	background-color: {ring(R_IN, R_OUT, "#5a3a12e6")};
}}

.nw-rim
{{
	width: 100%;
	height: 100%;
	visibility: collapse;
}}

/* An amber edge opens another wheel. */
.nw-wedge.cat .nw-rim
{{
	visibility: visible;
	background-color: {ring(RIM_IN, RIM_OUT, "#f99e2fd0")};
}}

.nw-wedge.pick .nw-rim
{{
	visibility: visible;
	background-color: {ring(RIM_IN, RIM_OUT, "#f99e2fff")};
}}

/* A green edge is a switch that is on. After the two above, so it wins. */
.nw-wedge.on .nw-rim
{{
	visibility: visible;
	background-color: {ring(RIM_IN, RIM_OUT, "#4ade80ff")};
}}

.nw-hit
{{
	horizontal-align: left;
	vertical-align: top;
	overflow: noclip;
}}

.nw-hit:hover
{{
	sound: "UIPanorama.generic_button_rollover";
}}

.nw-hit:active
{{
	sound: "UIPanorama.generic_button_click";
}}

/* Faded rather than switched, so sweeping the mouse round the wheel reads as
   one light moving and not a row of lamps flicking on and off. */
.nw-lit
{{
	width: {BOX}px;
	height: {BOX}px;
	horizontal-align: left;
	vertical-align: top;
	opacity: 0;
	background-color: {ring(R_IN, R_OUT, "#f99e2fd9")};
	transition-property: opacity;
	transition-duration: 0.12s;
	transition-timing-function: ease-out;
}}

.nw-hit:hover .nw-lit
{{
	opacity: 1;
}}

.nw-wedge.disabled .nw-hit:hover .nw-lit
{{
	opacity: 0;
}}

.nw-text
{{
	width: {LABEL_W}px;
	height: {LABEL_H}px;
	horizontal-align: left;
	vertical-align: top;
	flow-children: down;
}}

.nw-label
{{
	width: 100%;
	font-family: Stratum2, 'Arial Unicode MS';
	font-size: 18px;
	font-weight: bold;
	color: #fafafa;
	text-align: center;
	text-overflow: shrink;
	text-shadow: 1px 1px 2px 2 rgba(0, 0, 0, 0.8);
}}

.nw-sub
{{
	width: 100%;
	font-family: Stratum2 Mono, 'Arial Unicode MS';
	font-size: 11px;
	font-weight: bold;
	letter-spacing: 1.6px;
	text-transform: uppercase;
	color: #c4c4cc;
	text-align: center;
	text-overflow: shrink;
	text-shadow: 1px 1px 2px 2 rgba(0, 0, 0, 0.8);
}}

.nw-wedge.on .nw-sub
{{
	color: #4ade80;
}}

.nw-wedge.pick .nw-label
{{
	color: #f99e2f;
}}

/* The outer ring: up to two small things over each wedge, for what is worth
   one click without opening the wheel behind it. Built exactly like the
   wedges. Two share the width of the wedge; one on its own takes all of it,
   which is why every wedge has three of these and shows at most two. */
.nw-quick
{{
	width: 100%;
	height: 100%;
	flow-children: none;
}}

.nw-quick.blank
{{
	visibility: collapse;
}}

.nw-quick.disabled
{{
	opacity: 0.36;
}}

.nw-qfill
{{
	width: 100%;
	height: 100%;
	background-color: {ring(Q_IN, Q_OUT, "#0b0d10eb")};
}}

.nw-quick.on .nw-qfill
{{
	background-color: {ring(Q_IN, Q_OUT, "#10301ce0")};
}}

/* A hairline along the outside, so a quick action reads as a key cap and not
   a smudge of shadow. */
.nw-qedge
{{
	width: 100%;
	height: 100%;
	background-color: {ring(Q_OUT - 1.5, Q_OUT, "#ffffff2e")};
}}

.nw-quick.on .nw-qedge
{{
	background-color: {ring(Q_OUT - 2, Q_OUT, "#4ade80ff")};
}}

/* Hovered: a wash of amber with a bright edge. Not solid amber -- the label
   has to stay readable on it. */
.nw-qlit
{{
	width: {BOX}px;
	height: {BOX}px;
	horizontal-align: left;
	vertical-align: top;
	opacity: 0;
	background-color: {ring2(Q_IN, Q_OUT - 3, Q_OUT, "#f99e2f52", "#f99e2fff")};
	transition-property: opacity;
	transition-duration: 0.12s;
	transition-timing-function: ease-out;
}}

.nw-hit:hover .nw-qlit
{{
	opacity: 1;
}}

.nw-quick.disabled .nw-hit:hover .nw-qlit
{{
	opacity: 0;
}}

.nw-qlabel
{{
	width: {Q_LABEL_W}px;
	height: {Q_LABEL_H}px;
	horizontal-align: left;
	vertical-align: top;
	font-family: Stratum2, 'Arial Unicode MS';
	font-size: 14px;
	font-weight: normal;
	letter-spacing: 0.6px;
	color: #fafafa;
	text-align: center;
	text-overflow: shrink;
	text-shadow: 0px 1px 2px 1 rgba(0, 0, 0, 0.85);
}}

.nw-quick.on .nw-qlabel
{{
	color: #4ade80;
}}

/* The middle: back, or on the first wheel, close. */
.nw-hub
{{
	width: {HUB}px;
	height: {HUB}px;
	horizontal-align: center;
	vertical-align: center;
	flow-children: down;
	padding: 76px 14px 0px;
	background-color: #09090bf0;
	border: 2px solid #f99e2f8c;
	border-radius: 50%;
	transition-property: background-color, border;
	transition-duration: 0.12s;
	transition-timing-function: ease-out;
}}

.nw-hub:hover
{{
	background-color: #3a2710f5;
	border: 2px solid #f99e2f;
	sound: "UIPanorama.generic_button_rollover";
}}

.nw-hub:active
{{
	sound: "UIPanorama.generic_button_click";
}}

.nw-hub-title
{{
	width: 100%;
	font-family: Stratum2, 'Arial Unicode MS';
	font-size: 22px;
	font-weight: bold;
	color: #f99e2f;
	text-align: center;
	text-overflow: shrink;
}}

.nw-hub-sub
{{
	width: 100%;
	font-family: Stratum2 Mono, 'Arial Unicode MS';
	font-size: 11px;
	font-weight: bold;
	letter-spacing: 1.8px;
	text-transform: uppercase;
	color: #a1a1aa;
	text-align: center;
}}
''')

for slot in range(SLOTS):
    n = slot + 1
    css.append(f".nw-k{slot} .nw-fill, .nw-k{slot} .nw-rim, .nw-k{slot} .nw-lit {{ {clip(slot)} }}")
css.append("")

for slot in range(SLOTS):
    n = slot + 1
    angle = math.radians(slot * STEP)
    cx = C + LABEL_R * math.sin(angle)
    cy = C - LABEL_R * math.cos(angle)
    css.append(f".nw-k{slot} .nw-text {{ margin-left: {round(cx - LABEL_W / 2)}px; margin-top: {round(cy - LABEL_H / 2)}px; }}")
css.append("")

for slot in range(SLOTS):
    n = slot + 1
    for index, (x0, y0, x1, y1) in enumerate(hits(slot)):
        left = round(C + x0)
        top = round(C - y1)
        width = round(x1 - x0)
        height = round(y1 - y0)
        css.append(f"#r{n}h{index + 1} {{ margin-left: {left}px; margin-top: {top}px; width: {width}px; height: {height}px; }}")
        css.append(f"#r{n}h{index + 1} .nw-lit {{ margin-left: {-left}px; margin-top: {-top}px; }}")
css.append("")

for index in range(16):
    css.append(f".nw-q{index} .nw-qfill, .nw-q{index} .nw-qedge, .nw-q{index} .nw-qlit {{ {quick_clip(index)} }}")
css.append("")

for index in range(16):
    angle = math.radians(quick_centre(index))
    cx = C + Q_LABEL_R * math.sin(angle)
    cy = C - Q_LABEL_R * math.cos(angle)
    css.append(f".nw-q{index} .nw-qlabel {{ margin-left: {round(cx - Q_LABEL_W / 2)}px; margin-top: {round(cy - Q_LABEL_H / 2)}px; }}")
    css.append(f".nw-q{index} .nw-qlabel {{ {tangent(quick_centre(index))} }}")
css.append("")

for index in range(16):
    n = index + 1
    for hit, (x0, y0, x1, y1) in enumerate(quick_hits(index)):
        left = round(C + x0)
        top = round(C - y1)
        css.append(f"#o{n}h{hit + 1} {{ margin-left: {left}px; margin-top: {top}px; width: {round(x1 - x0)}px; height: {round(y1 - y0)}px; }}")
        css.append(f"#o{n}h{hit + 1} .nw-qlit {{ margin-left: {-left}px; margin-top: {-top}px; }}")
css.append("")

for slot in range(SLOTS):
    css.append(f".nw-x{slot} .nw-qfill, .nw-x{slot} .nw-qedge, .nw-x{slot} .nw-qlit {{ {wide_clip(slot)} }}")
css.append("")

for slot in range(SLOTS):
    angle = math.radians(slot * STEP)
    cx = C + Q_LABEL_R * math.sin(angle)
    cy = C - Q_LABEL_R * math.cos(angle)
    css.append(f".nw-x{slot} .nw-qlabel {{ margin-left: {round(cx - Q_LABEL_W / 2)}px; margin-top: {round(cy - Q_LABEL_H / 2)}px; }}")
    css.append(f".nw-x{slot} .nw-qlabel {{ {tangent(slot * STEP)} }}")
css.append("")

for slot in range(SLOTS):
    n = slot + 1
    for hit, (x0, y0, x1, y1) in enumerate(wide_hits(slot)):
        left = round(C + x0)
        top = round(C - y1)
        css.append(f"#x{n}h{hit + 1} {{ margin-left: {left}px; margin-top: {top}px; width: {round(x1 - x0)}px; height: {round(y1 - y0)}px; }}")
        css.append(f"#x{n}h{hit + 1} .nw-qlit {{ margin-left: {-left}px; margin-top: {-top}px; }}")
css.append("")

open(os.path.join(OUT, "layout/custom_game/nade2_wheel.xml"), "w").write("\n".join(xml))
open(os.path.join(OUT, "styles/custom_game/nadewheel2.css"), "w").write("\n".join(css))
print("hits per slot:", [len(hits(slot)) for slot in range(SLOTS)])
print("hits per quick:", [len(quick_hits(index)) for index in range(16)])
print("hits per wide:", [len(wide_hits(slot)) for slot in range(SLOTS)])

# a picture of the geometry, to look at before it goes anywhere near the game
def pt(r, deg):
    a = math.radians(deg)
    return C + r * math.sin(a), C - r * math.cos(a)

svg = [f'<svg xmlns="http://www.w3.org/2000/svg" width="{BOX}" height="{BOX}" viewBox="0 0 {BOX} {BOX}"><rect width="100%" height="100%" fill="#8fa3b8"/>']
names = ["Rethrow", "Clear utility", "Last throw", "Save throw", "Lineups", "Filters", "Practice", "Settings"]
subs = ["R", "SMOKE + FIRE", "BACK TO ITS SPOT", "KEEP THE LAST ONE", "16 OF 16", "ALL ›", "DRILL · BOTS ›", "VIEW · CHEATS ›"]
for slot in range(SLOTS):
    a0 = slot * STEP - STEP / 2 + GAP / 2
    a1 = slot * STEP + STEP / 2 - GAP / 2
    ax, ay = pt(R_OUT, a0); bx, by = pt(R_OUT, a1); cx, cy = pt(R_IN, a1); dx, dy = pt(R_IN, a0)
    svg.append(f'<path d="M{ax:.1f} {ay:.1f}A{R_OUT} {R_OUT} 0 0 1 {bx:.1f} {by:.1f}L{cx:.1f} {cy:.1f}A{R_IN} {R_IN} 0 0 0 {dx:.1f} {dy:.1f}Z" fill="#09090b" fill-opacity="0.88"/>')
    for (x0, y0, x1, y1) in hits(slot):
        svg.append(f'<rect x="{C + x0}" y="{C - y1}" width="{x1 - x0}" height="{y1 - y0}" fill="#f99e2f" fill-opacity="0.28" stroke="#f99e2f" stroke-width="0.6"/>')
    angle = math.radians(slot * STEP)
    lx = C + LABEL_R * math.sin(angle); ly = C - LABEL_R * math.cos(angle)
    svg.append(f'<rect x="{lx - LABEL_W / 2:.0f}" y="{ly - LABEL_H / 2:.0f}" width="{LABEL_W}" height="{LABEL_H}" fill="none" stroke="#4ade80" stroke-width="0.6" stroke-dasharray="3 3"/>')
    svg.append(f'<text x="{lx:.0f}" y="{ly - 2:.0f}" font-family="Arial" font-weight="700" font-size="18" fill="#fafafa" text-anchor="middle">{names[slot]}</text>')
    svg.append(f'<text x="{lx:.0f}" y="{ly + 14:.0f}" font-family="Menlo" font-weight="700" font-size="10.5" fill="#c4c4cc" text-anchor="middle">{subs[slot]}</text>')
qnames = ["", "Markers", "", "", "", "", "", "", "Prev", "Next", "Reset", "", "Noclip", "Bot", "", ""]
for index in range(16):
    centre = quick_centre(index)
    a0 = centre - Q_STEP / 2 + GAP / 2
    a1 = centre + Q_STEP / 2 - GAP / 2
    ax, ay = pt(Q_OUT, a0); bx, by = pt(Q_OUT, a1); cx, cy = pt(Q_IN, a1); dx, dy = pt(Q_IN, a0)
    svg.append(f'<path d="M{ax:.1f} {ay:.1f}A{Q_OUT} {Q_OUT} 0 0 1 {bx:.1f} {by:.1f}L{cx:.1f} {cy:.1f}A{Q_IN} {Q_IN} 0 0 0 {dx:.1f} {dy:.1f}Z" fill="#09090b" fill-opacity="{0.84 if qnames[index] else 0.25}"/>')
    for (x0, y0, x1, y1) in quick_hits(index):
        svg.append(f'<rect x="{C + x0}" y="{C - y1}" width="{x1 - x0}" height="{y1 - y0}" fill="#4ade80" fill-opacity="0.22" stroke="#4ade80" stroke-width="0.5"/>')
    lx, ly = pt(Q_LABEL_R, centre)
    svg.append(f'<text x="{lx:.0f}" y="{ly + 4:.0f}" font-family="Arial Narrow" font-weight="700" font-size="13" fill="#e4e4e7" text-anchor="middle">{qnames[index].upper()}</text>')
svg.append(f'<circle cx="{C}" cy="{C}" r="{HUB / 2}" fill="#09090b" stroke="#f99e2f" stroke-width="2"/><text x="{C}" y="{C + 4}" font-family="Arial" font-weight="700" font-size="22" fill="#f99e2f" text-anchor="middle">Practice</text><text x="{C}" y="{C + 24}" font-family="Menlo" font-size="10.5" fill="#a1a1aa" text-anchor="middle">CLOSE</text></svg>')
if SVG:
    open(SVG, "w").write("\n".join(svg))
