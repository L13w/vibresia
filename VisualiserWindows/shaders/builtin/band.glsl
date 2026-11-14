// -- Genre palette + motion (per-frame uniforms from Swift) --
uniform int uGenrePrev;         // outgoing genre during a cross-fade
uniform float uGenreMix;        // 0 = previous genre, 1 = current genre
uniform vec3 uPrimary;          // primary colour: curtain, lights, foreground
uniform vec3 uSecondary;        // secondary colour: floor, structural
uniform vec3 uAccent;           // accent colour: highlights, instruments
uniform float uBassReactivity;  // 0..1 — scales bass-driven deformation
uniform float uTrebleReactivity;// 0..1 — scales treble-driven deformation
uniform float uBeatSharpness;   // 0..1 — beat pulse sharpness
uniform float uStrobeIntensity;  // 0..1 — strobe overlay opacity

// -- Per-part + per-walker uniforms (from the Swift walker code) --
uniform float uParts[5];   // drums, bass, guitar, keys, vocals
uniform vec4 uHits;        // kick, snare, hi-hat, strum accents (smoothed)
uniform float uPluck;      // bass pluck accent
uniform float uVocal;      // vocal-range energy (smoothed)
uniform float uKeys[9];    // keyboard: energy per pitch range, low to high
// Walk-on / walk-off, per musician: 0 drums, 1 bass, 2 guitar, 3 keys,
// 4 female singer, 5 male singer.
uniform float uMX[6];      // current stage x (drummer: how far sunk behind the kit)
uniform float uHome[6];    // their mark (kit and keyboard stay there)
uniform float uWalk[6];    // walk cycle phase, or -1 when standing
uniform float uFace[6];    // walking direction -1 / +1, 0 standing
uniform float uPlay[6];    // how much they're playing right now
uniform int uLineupMask;   // bit per slot (0 drums … 5 male singer) in the genre's usual line-up
uniform float uPresent[6]; // 1 while that musician is on stage or walking on/off

// Cheap "genre-named" colours in case the palette uniforms don't make it
// (helps shaders run in isolation; always overridden at runtime). Hoisted
// before the room functions and ink/paint helpers so all of them can
// reference these constants in any order.
const vec3 INK = vec3(0.14, 0.07, 0.10);
const vec3 GENRE_RED    = vec3(0.62, 0.10, 0.14);
const vec3 GENRE_BLACK  = vec3(0.04, 0.04, 0.06);
const vec3 GENRE_GOLD   = vec3(0.95, 0.75, 0.25);
const vec3 GENRE_CYAN   = vec3(0.18, 0.95, 1.00);
const vec3 GENRE_MAGEN  = vec3(1.00, 0.18, 0.85);
const vec3 GENRE_WHITE  = vec3(0.95, 0.95, 0.92);
const vec3 GENRE_WOOD   = vec3(0.55, 0.33, 0.17);
const vec3 GENRE_WOOD_D = vec3(0.32, 0.20, 0.10);
const vec3 GENRE_AMBER  = vec3(0.90, 0.60, 0.25);
const vec3 GENRE_AMBER_D= vec3(0.55, 0.30, 0.10);
const vec3 GENRE_BROWN  = vec3(0.42, 0.24, 0.13);
const vec3 GENRE_PURPLE = vec3(0.45, 0.18, 0.50);
const vec3 GENRE_PURP_D = vec3(0.20, 0.08, 0.25);
const vec3 GENRE_PINK   = vec3(0.95, 0.55, 0.85);
const vec3 GENRE_BLUE_P = vec3(0.65, 0.75, 0.95);
const vec3 GENRE_GOLD_C = vec3(0.92, 0.78, 0.30);
const vec3 GENRE_AMBER_C= vec3(0.78, 0.55, 0.30);
const vec3 SKIN_A = vec3(0.98, 0.80, 0.64);
const vec3 SKIN_B = vec3(0.78, 0.55, 0.38);
const vec3 SKIN_C = vec3(0.52, 0.33, 0.22);
const float VH = 200.0;    // virtual screen height in art pixels

// Drawing primitives used by every genre's room. Hoisted to the top
// of the band body so the genre room functions can call them — the
// original code defined them further down and the GLSL compiler doesn't
// forward-reference function bodies.
void ink(inout vec3 col, float d, vec3 c) { if (d < 1.0) col = INK; if (d < 0.0) col = c; }
void paint(inout vec3 col, float d, vec3 c) { if (d < 0.0) col = c; }
// Forward declarations for helpers defined further down the file.
float swing(float ph);
vec2 onGuitar(vec2 at, float ang, vec2 local);
// Two-tone shading: darker on the right-hand side of a shape.
vec3 lit(vec3 c, float x, float edge) { return x > edge ? c * 0.78 : c; }
// Forward declarations for the person / arm / guitar / singer helpers
// that are defined further down. GLSL doesn't need the body to be in
// scope, only the signature.
void person(inout vec3 col, vec2 p, vec3 skin, vec3 hair, vec3 shirt, vec3 pants,
            float style, float mouth, bool legs, bool female, bool dress, float tap,
            float walk, float look);
void arm(inout vec3 col, vec2 p, vec2 s, vec2 h, float side, vec3 sleeve, vec3 skin);
void guitar(inout vec3 col, vec2 p, vec2 at, float ang, float dir, float neckLen, vec3 inner, vec3 outer);
void singer(inout vec3 col, vec2 v, int i, bool female, float voc, float sway);

// Helper: floor under a stage. Two-tone wood with the secondary palette
// colour as the dominant board.
float floorStripes(float y, float vmin, float vmax) {
    if (y < vmin) {
        float row = floor(y / 7.0);
        float a = mod(row, 2.0) < 1.0 ? 1.0 : 0.7;
        return a;
    }
    return 1.0;
}

// ========== Room 1: CLASSICAL — concert hall ==========
// Warm spotlights from above, four columns, marley floor, the band in formal
// attire (black suits, white shirts). Strings sit on the bass/guitar slots
// and bow with the beat.
vec3 roomClassical(vec2 v, vec3 col) {
    // hall background — a tall dim wall behind the performers
    if (v.y > 30.0) {
        float cy = (v.y - 30.0) / 80.0;
        col = mix(vec3(0.30, 0.18, 0.10), vec3(0.10, 0.05, 0.03), cy);
    }
    // wooden marley floor
    if (v.y < -30.0) {
        float row = floor(v.y / 6.0);
        col = mod(row, 2.0) < 1.0 ? GENRE_WOOD : GENRE_WOOD_D;
        if (mod(v.y, 6.0) < 0.8) col = vec3(0.20, 0.10, 0.05);
    }
    // organ pipes across the upper wall, glinting with the bass
    if (v.y > 30.0 && v.y < 64.0) {
        float px = mod(v.x + 300.0, 10.0) - 5.0;
        float idx = floor((v.x + 300.0) / 10.0);
        float top = 36.0 + 20.0 * (0.5 + 0.5 * cos(idx * 0.42)) ;
        if (abs(px) < 3.5 && v.y < top) {
            col = px < -1.0 ? vec3(0.95, 0.82, 0.45) : (px < 1.5 ? vec3(0.78, 0.62, 0.28) : vec3(0.55, 0.40, 0.16));
            col *= 0.85 + 0.3 * uBass;
            if (abs(v.y - (top - 3.0)) < 0.8) col = vec3(0.20, 0.10, 0.05);   // pipe mouth
        }
    }
    // four columns at the back
    for (int i = 0; i < 4; i++) {
        float cx = (float(i) - 1.5) * 110.0;
        float c = sdBox(v - vec2(cx, 30.0), vec2(7.0, 60.0));
        ink(col, c, GENRE_GOLD_C);
        ink(col, max(c, abs(v.x - cx) - 6.0) - 1.0, GENRE_AMBER_C);
    }
    // spotlight cones from above
    for (int i = 0; i < 3; i++) {
        float cx = (float(i) - 1.0) * 130.0;
        float cone = max(abs(v.x - cx) - abs(v.y - 60.0) * 0.7, v.y - 60.0);
        if (cone < 0.0) col = mix(col, GENRE_GOLD_C, 0.10);
    }
    // gold valance at the top
    float valY = 64.0;
    if (v.y > valY) {
        // a gold trim band, then a dark coffered ceiling above it
        col = v.y < valY + 8.0 ? vec3(0.55, 0.42, 0.10) : mix(vec3(0.16, 0.09, 0.05), vec3(0.06, 0.03, 0.02), clamp((v.y - valY) / 60.0, 0.0, 1.0));
        if (v.y > valY + 8.0 && (mod(v.x, 40.0) < 1.0 || mod(v.y - valY, 20.0) < 1.0)) col *= 0.6;
    }
    if (abs(v.y - valY) < 1.5 || abs(v.y - valY - 8.0) < 1.0) col = GENRE_GOLD_C;
    return col;
}

// ========== Room 2: ELECTRONIC — DJ booth ==========
// Dark room with neon strips, two turntables, a mixer, a single DJ. Beat
// strobes pulse on every kick. Cyan/magenta dominates.
vec3 roomElectronic(vec2 v, vec3 col) {
    // dark background
    if (v.y > 0.0) {
        col = mix(vec3(0.02, 0.01, 0.04), vec3(0.05, 0.02, 0.10), v.y / 100.0);
    }
    // floor
    if (v.y < 0.0) {
        float row = floor(v.y / 5.0);
        col = mod(row, 2.0) < 1.0 ? vec3(0.06, 0.04, 0.10) : vec3(0.10, 0.06, 0.16);
    }
    // neon strips on the back wall, vertical, alternating cyan/magenta
    for (int i = 0; i < 5; i++) {
        float cx = (float(i) - 2.0) * 80.0;
        float s = abs(v.x - cx) - 1.5;
        if (s < 0.0 && v.y > 0.0 && v.y < 60.0) {
            vec3 neon = mod(float(i), 2.0) < 1.0 ? GENRE_CYAN : GENRE_MAGEN;
            float pulse = 0.6 + 0.4 * sin(uTime * 6.0 + float(i));
            col = mix(col, neon, pulse);
        }
    }
    // LED wall: a grid of tiles lit as a spectrum, cyan to magenta
    if (v.y > 4.0 && v.y < 58.0) {
        vec2 cell = floor(vec2(v.x + 400.0, v.y - 4.0) / 6.0);
        vec2 inCell = mod(vec2(v.x + 400.0, v.y - 4.0), 6.0);
        float cols = 800.0 / 6.0;
        float level = bandAt(fract(cell.x / cols * 2.0)) * 9.0 * uActive;
        if (inCell.x > 0.8 && inCell.y > 0.8 && inCell.x < 5.2 && inCell.y < 5.2) {
            bool on = cell.y < level;
            vec3 led = mix(GENRE_CYAN, GENRE_MAGEN, clamp(cell.y / 8.0, 0.0, 1.0));
            col = on ? led : mix(col, led, 0.06);
        }
    }
    // ceiling strip
    if (v.y > 60.0 && v.y < 66.0) col = mix(GENRE_CYAN, GENRE_MAGEN, 0.5 + 0.5 * sin(uTime * 2.0));
    return col;
}

// ========== Room 3: ROCK — theatre (the original) ==========
// Velvet curtain with a gold valance, wooden stage, amp stacks, full
// 5-piece band. This branch is the long one because it was the first
// and has the most detail.
vec3 roomRock(vec2 v, vec3 col) {
    // red velvet curtain
    float fold = sin(v.x * 0.33);
    col = fold > 0.5 ? vec3(0.62, 0.10, 0.14) : (fold > -0.4 ? vec3(0.50, 0.07, 0.11) : vec3(0.36, 0.04, 0.08));
    float val = 78.0 + 5.0 * abs(sin(v.x * 0.105));
    if (v.y > val) col = mod(v.x, 10.0) < 1.0 ? vec3(0.40, 0.05, 0.09) : vec3(0.55, 0.08, 0.12);
    if (abs(v.y - val) < 1.5) col = vec3(0.95, 0.75, 0.25);
    // wooden stage
    float floorY = -40.0;
    if (v.y < floorY) {
        float row = floor(v.y / 7.0);
        col = mod(row, 2.0) < 1.0 ? GENRE_WOOD : GENRE_WOOD_D;
        if (mod(v.y, 7.0) < 1.0 || mod(v.x + row * 13.0, 36.0) < 1.0) col = vec3(0.38, 0.21, 0.11);
    }
    if (abs(v.y - floorY) < 1.0) col = vec3(0.30, 0.15, 0.08);
    float edgeY = -88.0;
    if (v.y < edgeY) col = vec3(0.10, 0.05, 0.07);
    if (abs(v.y - edgeY) < 1.0) col = vec3(0.95, 0.75, 0.25);
    vec2 fl = vec2(mod(v.x, 30.0) - 15.0, v.y - (edgeY - 4.0));
    ink(col, sdEllipse(fl, vec2(4.0, 2.5)), vec3(1.0, 0.88, 0.45));
    // amp stacks
    for (int side = -1; side <= 1; side += 2) {
        vec2 a = v - vec2(float(side) * 232.0, -10.0);
        for (int k = 0; k < 2; k++) {
            vec2 c = a - vec2(0.0, k == 0 ? -22.0 : 6.0);
            float box = sdRoundBox(c, vec2(24.0, 13.0), 2.0);
            vec3 grille = mod(floor(c.x) + floor(c.y), 2.0) < 1.0 ? vec3(0.16, 0.15, 0.16) : vec3(0.22, 0.21, 0.22);
            grille *= 1.0 + uBass * 0.35 * uActive;
            ink(col, box, grille);
            if (box < 0.0 && abs(c.y - 10.0) < 1.0) col = vec3(0.85, 0.68, 0.25);
        }
    }
    return col;
}

// ========== Room 4: POP — clean studio ==========
// Pink/purple gradient backdrop, a single mic on a stand, clean floor.
vec3 roomPop(vec2 v, vec3 col) {
    if (v.y > 0.0) {
        // gradient from purple at top to pink at floor
        col = mix(vec3(0.42, 0.25, 0.55), vec3(0.95, 0.65, 0.85), clamp(1.0 - v.y / 80.0, 0.0, 1.0));
    }
    if (v.y < 0.0) {
        // glossy white floor
        col = mix(vec3(0.85, 0.85, 0.88), vec3(0.70, 0.70, 0.75), abs(v.y) / 30.0);
        if (mod(v.y, 8.0) < 1.0) col *= 0.9;
    }
    // twinkling star lights across the backdrop, blinking in time
    if (v.y > 6.0) {
        vec2 cell = floor(v / 14.0);
        vec2 sp = mod(v, 14.0) - 7.0 + (vec2(hash21(cell), hash21(cell + 3.1)) - 0.5) * 8.0;
        float on = step(0.5, fract(hash21(cell + 7.7) + floor(uBeats) * 0.37));
        float star = min(abs(sp.x) + abs(sp.y) * 3.0, abs(sp.y) + abs(sp.x) * 3.0);
        if (star < 2.0 && on > 0.5) col = mix(col, vec3(1.0, 0.95, 1.0), 0.85);
    }
    return col;
}

// ========== Room 5: HIP-HOP — urban ==========
// Brick wall, gold chain curtain, low-rider, single MC at the mic and
// a DJ behind decks. Heavy bass-driven low-end rumble.
vec3 roomHipHop(vec2 v, vec3 col) {
    // brick wall above
    if (v.y > 0.0) {
        float bx = mod(v.x, 18.0);
        float by = mod(v.y, 9.0);
        float brick = step(9.0, mod(v.y, 18.0)) > 0.5 ? bx - 9.0 : bx;
        col = vec3(0.32, 0.14, 0.10);
        if (bx < 0.8 || by < 0.8) col = vec3(0.18, 0.08, 0.06);
    }
    if (v.y < 0.0) {
        // dark concrete floor
        col = mix(vec3(0.06, 0.05, 0.04), vec3(0.02, 0.02, 0.02), abs(v.y) / 30.0);
    }
    // graffiti: bubbly tags with drips, in the palette colours
    for (int i = 0; i < 4; i++) {
        float fi = float(i);
        vec2 c = vec2(-180.0 + fi * 120.0, 30.0 + 10.0 * sin(fi * 2.1));
        vec3 paint1 = i % 2 == 0 ? uPrimary : uAccent;
        float tag = 1e3;
        for (int j = 0; j < 4; j++) {
            float fj = float(j);
            tag = min(tag, sdCircle(v - c - vec2(fj * 11.0 - 16.0, 4.0 * sin(fj * 1.7 + fi)), 7.0 - mod(fj, 2.0)));
        }
        if (v.y > 0.0) {
            ink(col, tag, paint1);
            if (tag < -3.5) col = mix(paint1, vec3(1.0), 0.5);   // highlight
            float drip = abs(v.x - c.x - 6.0) - 0.8;
            if (drip < 0.0 && v.y < c.y - 6.0 && v.y > c.y - 16.0 - 4.0 * fi) col = paint1;
        }
    }
    return col;
}

// ========== Room 6: JAZZ — smoky club ==========
// Dim purple/amber. Single spotlight, smoke particles, upright bass, sax.
vec3 roomJazz(vec2 v, vec3 col) {
    // smoky background
    if (v.y > 0.0) {
        col = mix(vec3(0.20, 0.10, 0.15), vec3(0.05, 0.02, 0.05), v.y / 100.0);
    }
    if (v.y < 0.0) {
        col = mix(vec3(0.30, 0.18, 0.12), vec3(0.10, 0.05, 0.04), abs(v.y) / 30.0);
    }
    // spotlight from above
    float spotX = 0.0;
    float spotD = max(abs(v.x - spotX) - abs(v.y - 50.0) * 0.5, v.y - 50.0);
    if (spotD < 0.0) col = mix(col, GENRE_AMBER, 0.30);
    // drifting cigarette smoke
    float smoke = sin(v.x * 0.03 + uTime * 0.3 + sin(v.y * 0.05)) * sin(v.y * 0.04 - uTime * 0.2);
    if (v.y > 0.0) col = mix(col, vec3(0.55, 0.45, 0.50), smoothstep(0.55, 1.0, smoke) * 0.25);
    // little round tables with candles along the front
    for (int i = 0; i < 5; i++) {
        vec2 t = v - vec2(-200.0 + float(i) * 100.0, -93.0);
        float table = min(sdEllipse(t - vec2(0.0, 8.0), vec2(16.0, 3.0)), sdBox(t, vec2(1.5, 8.0)));
        if (table < 0.0) col = vec3(0.04, 0.02, 0.03);
        float flame = sdEllipse(t - vec2(0.0, 13.0 + 0.5 * sin(uTime * 9.0 + float(i))), vec2(1.0, 1.8));
        if (flame < 0.0) col = vec3(1.0, 0.75, 0.30);
    }
    return col;
}

// ========== Room 7: BLUES — bar ==========
// Wood, neon "BAR" sign, a bottle, dim amber.
vec3 roomBlues(vec2 v, vec3 col) {
    // wood wall
    col = vec3(0.30, 0.18, 0.10);
    if (mod(v.x, 16.0) < 0.8) col = vec3(0.18, 0.10, 0.06);
    if (mod(v.y, 24.0) < 0.8) col = vec3(0.18, 0.10, 0.06);
    // neon sign: a guitar in a rounded frame, flickering with the beat
    {
        vec2 n = v - vec2(140.0, 42.0);
        float flick = 0.75 + 0.25 * step(0.15, fract(uBeats * 0.5 + 0.1));
        vec3 red = vec3(1.0, 0.25, 0.20) * flick, blue = vec3(0.35, 0.55, 1.0) * flick;
        float frame = abs(sdRoundBox(n, vec2(26.0, 16.0), 5.0)) - 0.9;
        if (frame < 0.0) col = blue;
        float gtr = abs(min(sdCircle(n - vec2(-6.0, -4.0), 6.0), sdCircle(n - vec2(1.0, 0.0), 4.5))) - 0.8;
        gtr = min(gtr, sdSeg(n, vec2(4.0, 3.0), vec2(18.0, 11.0)) - 0.8);
        if (gtr < 0.0) col = red;
        if (min(frame, gtr) < 2.5 && min(frame, gtr) >= 0.0) col = mix(col, red, 0.15);   // glow
    }
    // bar counter
    if (v.y < -10.0 && v.y > -22.0) {
        col = vec3(0.20, 0.10, 0.05);
        if (mod(v.x, 30.0) < 0.8) col = vec3(0.10, 0.05, 0.02);
    }
    return col;
}

// ========== Room 8: COUNTRY — porch ==========
// Wood floor, daylight, an old window, simple. Warm and friendly.
vec3 roomCountry(vec2 v, vec3 col) {
    // sky behind
    if (v.y > 30.0) {
        col = mix(vec3(0.65, 0.80, 0.95), vec3(0.95, 0.85, 0.65), 1.0 - v.y / 100.0);
    }
    if (v.y < 30.0 && v.y > 0.0) {
        // sunlit wood wall
        col = mix(vec3(0.55, 0.36, 0.20), vec3(0.70, 0.48, 0.28), 1.0 - v.y / 30.0);
        if (mod(v.x, 12.0) < 0.8) col = vec3(0.32, 0.20, 0.10);
    }
    if (v.y < 0.0) {
        col = mix(GENRE_WOOD, GENRE_WOOD_D, abs(v.y) / 30.0);
        if (mod(v.y, 8.0) < 0.8) col *= 0.7;
    }
    // a split-rail fence on the horizon and rolling hills behind it
    float hill = 34.0 + 6.0 * sin(v.x * 0.012) + 3.0 * sin(v.x * 0.031 + 1.0);
    if (v.y > 30.0 && v.y < hill) col = mix(vec3(0.45, 0.62, 0.30), vec3(0.62, 0.74, 0.40), (v.y - 30.0) / 10.0);
    if (v.y > 30.0 && v.y < 44.0) {
        if (mod(v.x, 40.0) < 2.0) col = vec3(0.40, 0.26, 0.14);
        if (abs(v.y - 36.0) < 0.8 || abs(v.y - 41.0) < 0.8) col = vec3(0.48, 0.32, 0.18);
    }
    // a window in the porch wall, glowing warm
    vec2 w = v - vec2(-160.0, 14.0);
    if (abs(w.x) < 13.0 && abs(w.y) < 10.0) {
        col = vec3(1.0, 0.88, 0.55);
        if (abs(w.x) < 0.8 || abs(w.y) < 0.8 || abs(w.x) > 11.5 || abs(w.y) > 8.5) col = vec3(0.30, 0.18, 0.10);
    }
    // hay bales stage left and right
    for (int i = -1; i <= 1; i += 2) {
        vec2 h = v - vec2(float(i) * 225.0, -48.0);
        float bale = sdRoundBox(h, vec2(18.0, 9.0), 2.0);
        ink(col, bale, vec3(0.88, 0.72, 0.35));
        if (bale < 0.0 && mod(h.x + h.y * 0.3, 4.0) < 0.8) col = vec3(0.72, 0.56, 0.22);
    }
    return col;
}

// ========== Room 9: R&B — velvet stage ==========
// Purple velvet curtain, gold trim, multiple spotlights. Five-piece band.
vec3 roomRNB(vec2 v, vec3 col) {
    // velvet curtain
    float fold = sin(v.x * 0.28);
    col = fold > 0.0 ? vec3(0.40, 0.15, 0.42) : vec3(0.28, 0.08, 0.30);
    // gold valance
    float val = 75.0;
    if (v.y > val) col = mix(GENRE_PURPLE, vec3(0.18, 0.05, 0.20), 0.3);
    if (abs(v.y - val) < 1.5) col = GENRE_GOLD_C;
    // gold tassels
    for (int i = 0; i < 12; i++) {
        float tx = (float(i) - 5.5) * 30.0;
        float tc = sdCircle(v - vec2(tx, val + 3.0), 2.0);
        if (tc < 0.0) col = GENRE_GOLD_C;
    }
    // floor
    if (v.y < -30.0) {
        col = mix(GENRE_WOOD, GENRE_WOOD_D, abs(v.y + 30.0) / 30.0);
    }
    // multiple soft spotlights
    for (int i = 0; i < 4; i++) {
        float sx = (float(i) - 1.5) * 90.0;
        float sd = max(abs(v.x - sx) - abs(v.y - 55.0) * 0.6, v.y - 55.0);
        if (sd < 0.0) col = mix(col, GENRE_GOLD_C, 0.10);
    }
    return col;
}

// ========== Room 10: AMBIENT — pure soundscape ==========
// No stage, no band. Soft colour field that breathes with the bass, with
// grainy nebula-like wisps in the treble. No lfbm — we use a few cheap
// sine fields instead since the band prelude doesn't pull in the
// FBM stack.
vec3 roomAmbient(vec2 v, vec3 col) {
    // base colour: a slow, warm gradient driven by uTime
    vec3 a = mix(uPrimary, uSecondary, 0.5 + 0.5 * sin(uTime * 0.15));
    vec3 b = mix(uAccent, uPrimary, 0.5 + 0.5 * cos(uTime * 0.21));
    float t = clamp(v.y / 100.0, 0.0, 1.0);
    col = mix(b, a, t);
    // wisps: a few overlapping sine fields, weighted by treble
    float w1 = sin(v.x * 0.022 + uTime * 0.10) * cos(v.y * 0.018 - uTime * 0.07);
    float w2 = sin((v.x + v.y) * 0.014 + uTime * 0.05);
    float wisps = smoothstep(0.4, 0.9, w1 * 0.6 + w2 * 0.4) *
                  (0.4 + 0.6 * uTrebleReactivity * (0.3 + uTreble));
    col = mix(col, uAccent, wisps * 0.6);
    // bass breath: a soft radial pulse that grows with uBass
    float r = length(v - vec2(0.0, -10.0));
    float pulse = exp(-r * 0.018) * (0.4 + 0.6 * uBassReactivity * (0.3 + uBass));
    col += uAccent * pulse * 0.5;
    return col;
}

// Draw one genre's room. Split out from pickRoom so the cross-fade can
// call it for two genres and blend the results.
vec3 roomForGenre(int g, vec2 v) {
    vec3 col = vec3(0.02, 0.004, 0.03);
    if (g == 0)      { col = roomClassical(v, col);  }
    else if (g == 1) { col = roomElectronic(v, col); }
    else if (g == 2) { col = roomRock(v, col);      }
    else if (g == 3) { col = roomPop(v, col);       }
    else if (g == 4) { col = roomHipHop(v, col);    }
    else if (g == 5) { col = roomJazz(v, col);      }
    else if (g == 6) { col = roomBlues(v, col);     }
    else if (g == 7) { col = roomCountry(v, col);   }
    else if (g == 8) { col = roomRNB(v, col);       }
    else if (g == 9) { col = roomAmbient(v, col);   }
    return col;
}

// Top-level room dispatch, with a cross-fade when uGenre != uGenrePrev.
vec3 pickRoom(vec2 v) {
    if (uGenrePrev == uGenre || uGenreMix >= 1.0) {
        return roomForGenre(uGenre, v);
    }
    vec3 from = roomForGenre(uGenrePrev, v);
    vec3 to = roomForGenre(uGenre, v);
    return mix(from, to, uGenreMix);
}

// Beat-strobe overlay drawn on top of any room when strobeIntensity is
// high. A bright full-screen flash that fades with each beat.
void strobeOverlay(vec2 v, inout vec3 col) {
    if (uStrobeIntensity < 0.05) return;
    // only on the downbeat: weight by the per-beat envelope
    float beat = fract(uBeats);
    float env = exp(-beat * 8.0) * smoothstep(0.0, 0.05, beat);
    col = mix(col, mix(uPrimary, uAccent, 0.5), env * uStrobeIntensity * 0.65);
}


// ---------- Genre outfits & instruments ----------

// Hats, drawn on a head whose centre is h (person-local p - (0, 50)).
// kind: 0 none, 1 cowboy, 2 fedora, 3 cap, 4 beret, 5 visor shades
void hat(inout vec3 col, vec2 h, float kind, vec3 c) {
    if (kind < 0.5) return;
    if (kind < 1.5) {
        ink(col, sdEllipse(h - vec2(0.0, 5.5), vec2(15.0, 2.2)), c * 0.85);
        ink(col, sdRoundBox(h - vec2(0.0, 9.5), vec2(7.0, 4.5), 2.5), c);
        paint(col, sdBox(h - vec2(0.0, 6.8), vec2(7.0, 0.8)), c * 0.55);
    } else if (kind < 2.5) {
        ink(col, sdEllipse(h - vec2(0.0, 5.0), vec2(12.0, 1.8)), c * 0.85);
        ink(col, sdRoundBox(h - vec2(0.0, 9.0), vec2(7.5, 4.0), 2.0), c);
        paint(col, sdBox(h - vec2(0.0, 6.5), vec2(7.5, 1.0)), vec3(0.60, 0.12, 0.12));
    } else if (kind < 3.5) {
        ink(col, max(sdCircle(h - vec2(0.0, 2.0), 9.0), -(h.y - 4.0)), c);
        ink(col, sdEllipse(h - vec2(8.5, 4.0), vec2(6.0, 1.5)), c * 0.8);
    } else if (kind < 4.5) {
        ink(col, sdEllipse(h - vec2(-1.5, 8.0), vec2(9.5, 3.5)), c);
    } else {
        ink(col, sdRoundBox(h - vec2(0.0, 0.3), vec2(7.0, 1.8), 1.0), c);
    }
}

// Per-genre outfit for a slot: shirt, pants, hair colour, hair style,
// hat kind, hat colour, and extras (x: gold chain, y: shirt-front stripe,
// z: sparkles, w: check pattern).
struct Outfit { vec3 shirt; vec3 pants; vec3 hair; float style; float hatKind; vec3 hatCol; vec4 extra; };

Outfit outfit(int g, int slot) {
    // rock defaults (the theatre band)
    vec3 shirts[6] = vec3[6](vec3(0.95, 0.85, 0.30), vec3(0.25, 0.55, 0.85), vec3(0.20, 0.60, 0.45),
                             vec3(0.65, 0.35, 0.85), vec3(0.45, 0.15, 0.42), vec3(0.16, 0.16, 0.19));
    vec3 hairs[6] = vec3[6](vec3(0.20, 0.12, 0.08), vec3(0.10, 0.07, 0.06), vec3(0.62, 0.24, 0.10),
                            vec3(0.20, 0.12, 0.08), vec3(0.95, 0.85, 0.55), vec3(0.28, 0.16, 0.08));
    float styles[6] = float[6](1.0, 2.0, 3.0, 4.0, 3.0, 2.0);
    Outfit o = Outfit(shirts[slot], vec3(0.22, 0.24, 0.40), hairs[slot], styles[slot], 0.0, vec3(0.0), vec4(0.0));
    if (slot == 3) o.style = 4.0;
    if (g == 0) {            // classical: black tails, white shirt fronts
        o.shirt = vec3(0.07, 0.07, 0.09); o.pants = vec3(0.05);
        if (slot == 2 || slot == 4) o.shirt = vec3(0.42, 0.05, 0.10);   // the women in deep red
        o.extra.y = (slot == 2 || slot == 4) ? 0.0 : 1.0;
        if (slot == 3) o.style = 2.0;
    } else if (g == 1) {     // electronic: black with neon trim, visor shades
        o.shirt = vec3(0.06, 0.06, 0.09); o.pants = vec3(0.04);
        o.hatKind = 5.0; o.hatCol = uAccent; o.extra.y = 2.0;
        if (slot == 3) o.style = 0.0;
    } else if (g == 3) {     // pop: bright pastels with sparkles
        o.shirt = mix(uPrimary, vec3(1.0), 0.35 + 0.1 * float(slot % 3));
        o.pants = mix(uSecondary, vec3(1.0), 0.3); o.extra.z = 1.0;
        if (slot == 3) o.style = 3.0;
    } else if (g == 4) {     // hip-hop: hoodies, caps, chains
        o.shirt = mix(uSecondary, vec3(0.15), 0.3 + 0.2 * float(slot % 2));
        o.pants = vec3(0.12, 0.12, 0.14); o.hatKind = slot == 4 ? 0.0 : 3.0;
        o.hatCol = mix(uPrimary, uAccent, float(slot % 2)); o.extra.x = 1.0;
    } else if (g == 5) {     // jazz: suits, berets and fedoras
        o.shirt = slot % 2 == 0 ? vec3(0.18, 0.12, 0.22) : vec3(0.12, 0.12, 0.18);
        if (slot == 2 || slot == 4) o.shirt = vec3(0.50, 0.12, 0.25);
        o.pants = vec3(0.08); o.hatKind = slot == 3 ? 4.0 : (slot == 1 ? 2.0 : 0.0);
        o.hatCol = vec3(0.12, 0.10, 0.12); o.extra.y = slot == 1 || slot == 3 ? 1.0 : 0.0;
    } else if (g == 6) {     // blues: fedoras and waistcoats
        o.shirt = vec3(0.30, 0.20, 0.12); o.pants = vec3(0.14, 0.12, 0.12);
        o.hatKind = slot == 2 || slot == 4 ? 0.0 : 2.0; o.hatCol = vec3(0.20, 0.16, 0.12);
        if (slot == 2 || slot == 4) o.shirt = vec3(0.25, 0.30, 0.55);
    } else if (g == 7) {     // country: cowboy hats and check shirts
        o.shirt = slot % 2 == 0 ? vec3(0.70, 0.18, 0.15) : vec3(0.20, 0.35, 0.60);
        o.pants = vec3(0.22, 0.30, 0.52); o.hatKind = 1.0;
        o.hatCol = slot % 2 == 0 ? vec3(0.62, 0.42, 0.22) : vec3(0.92, 0.88, 0.80);
        o.extra.w = 1.0;
        if (slot == 2 || slot == 4) o.shirt = vec3(0.85, 0.75, 0.55);
    } else if (g == 8) {     // R&B: purple and gold suits
        o.shirt = slot % 2 == 0 ? vec3(0.40, 0.12, 0.45) : vec3(0.85, 0.68, 0.25);
        o.pants = vec3(0.10, 0.05, 0.12); o.extra.x = slot == 5 ? 1.0 : 0.0;
    }
    return o;
}

// Outfit details over a drawn person (p = person-local, feet at origin).
void outfitExtras(inout vec3 col, vec2 p, Outfit o) {
    if (o.extra.y > 1.5) {          // neon trim across the chest
        if (abs(p.y - 30.0) < 0.8 && abs(p.x) < 9.0) col = uAccent;
    } else if (o.extra.y > 0.5) {   // white shirt front + bow tie
        paint(col, sdBox(p - vec2(0.0, 32.0), vec2(2.5, 7.0)), vec3(0.95, 0.95, 0.92));
        paint(col, sdBox(p - vec2(0.0, 38.0), vec2(2.0, 0.8)), INK);
    }
    if (o.extra.x > 0.5) {          // gold chain
        float e = abs(sdEllipse(p - vec2(0.0, 35.0), vec2(5.0, 6.0)));
        if (e < 0.7 && p.y < 38.0) col = vec3(0.98, 0.80, 0.25);
    }
    if (o.extra.z > 0.5 && sdRoundBox(p - vec2(0.0, 29.0), vec2(10.0, 11.0), 4.0) < -0.5) {
        if (hash21(floor(p * 0.5) + floor(uBeats * 2.0)) > 0.86) col = vec3(1.0);   // sparkles, twinkling in time
    }
    if (o.extra.w > 0.5 && sdRoundBox(p - vec2(0.0, 29.0), vec2(10.0, 11.0), 4.0) < -0.5) {
        if (mod(floor(p.x / 3.0) + floor(p.y / 3.0), 2.0) < 1.0) col *= 0.75;      // check shirt
    }
    hat(col, p - vec2(0.0, 50.0), o.hatKind, o.hatCol);
}

// Standard musician: outfit, walking legs, look direction.
void musician(inout vec3 col, vec2 p, Outfit o, vec3 skin, float mouth, bool female, float tap, float walk, float look) {
    person(col, p, skin, o.hair, o.shirt, o.pants, o.style, mouth, true, female, female, tap, walk, look);
    outfitExtras(col, p, o);
}

// Violin under the chin, bowed by the right hand (p = player-local).
void violin(inout vec3 col, vec2 p, float saw, vec3 skin, vec3 sleeve) {
    arm(col, p, vec2(-9.0, 36.0), vec2(-19.0, 43.0), -1.0, sleeve, skin);
    vec2 q = rot(-0.35) * (p - vec2(-8.0, 40.0));
    ink(col, min(sdEllipse(q - vec2(-2.0, 0.0), vec2(5.0, 3.2)), sdEllipse(q - vec2(3.0, 0.0), vec2(4.0, 2.8))), vec3(0.62, 0.30, 0.10));
    ink(col, sdSeg(p, vec2(-12.0, 41.5), vec2(-22.0, 45.0)) - 0.9, vec3(0.20, 0.10, 0.05));
    vec2 hand = mix(vec2(2.0, 34.0), vec2(14.0, 30.0), saw);
    arm(col, p, vec2(9.0, 36.0), hand, 1.0, sleeve, skin);
    ink(col, sdSeg(p, hand, hand + vec2(-24.0, 8.0)) - 0.4, vec3(0.80, 0.70, 0.50));
}

// Cello / double bass standing in front of the player. bowed: classical.
void bigString(inout vec3 col, vec2 p, bool bowed, float motion, vec3 skin, vec3 sleeve) {
    vec3 wood = bowed ? vec3(0.55, 0.25, 0.08) : vec3(0.45, 0.22, 0.10);
    ink(col, sdSeg(p, vec2(6.0, 0.0), vec2(6.0, 8.0)) - 0.6, vec3(0.5));
    float body = min(sdEllipse(p - vec2(6.0, 20.0), vec2(10.0, 11.0)), sdEllipse(p - vec2(6.0, 34.0), vec2(8.0, 7.5)));
    body = min(body, sdBox(p - vec2(6.0, 27.0), vec2(6.5, 4.0)));
    ink(col, body, wood);
    if (body < -1.0 && abs(abs(p.x - 6.0) - 4.0) < 0.6 && abs(p.y - 25.0) < 4.0) col = INK;   // f-holes
    ink(col, sdSeg(p, vec2(6.0, 40.0), vec2(6.0, 66.0)) - 1.1, vec3(0.15, 0.08, 0.04));
    ink(col, sdCircle(p - vec2(6.0, 68.0), 2.0), wood);
    arm(col, p, vec2(-9.0, 36.0), vec2(3.0, 56.0 - motion * 3.0), -1.0, sleeve, skin);
    vec2 hand = bowed ? mix(vec2(-4.0, 22.0), vec2(18.0, 24.0), motion) : vec2(10.0 + motion * 2.0, 26.0 - motion * 2.0);
    arm(col, p, vec2(9.0, 36.0), hand, 1.0, sleeve, skin);
    if (bowed) ink(col, sdSeg(p, hand + vec2(-10.0, 0.5), hand + vec2(12.0, -0.5)) - 0.4, vec3(0.80, 0.70, 0.50));
}

// Saxophone, played at the mouth (p = player-local).
void sax(inout vec3 col, vec2 p, float fingers, vec3 skin, vec3 sleeve) {
    vec3 brass = vec3(0.92, 0.72, 0.28);
    ink(col, sdSeg(p, vec2(1.0, 45.0), vec2(4.0, 34.0)) - 1.2, brass);
    ink(col, sdSeg(p, vec2(4.0, 34.0), vec2(6.0, 18.0)) - 2.0, brass);
    ink(col, sdSeg(p, vec2(6.0, 18.0), vec2(12.0, 16.0)) - 2.6, brass);
    ink(col, sdSeg(p, vec2(12.0, 16.0), vec2(15.0, 24.0)) - 3.0, brass);
    ink(col, sdEllipse(p - vec2(15.5, 26.0), vec2(4.5, 1.6)), brass * 0.8);
    for (int k = 0; k < 4; k++) paint(col, sdCircle(p - vec2(6.6, 22.0 + float(k) * 4.0), 0.9), brass * 0.6);
    arm(col, p, vec2(-9.0, 36.0), vec2(3.0, 37.0 + fingers), -1.0, sleeve, skin);
    arm(col, p, vec2(9.0, 36.0), vec2(8.0, 26.0 - fingers), 1.0, sleeve, skin);
}

// Keyboard-slot instruments, centred on k (keyboard-local, floor at 0).
// kind: 0 synth on X-stand, 1 grand piano, 2 upright piano, 3 DJ decks
void keysInstrument(inout vec3 col, vec2 k, float kind, float pKe) {
    if (kind < 0.5) {
        ink(col, sdSeg(k, vec2(-15.0, 0.0), vec2(15.0, 27.0)) - 0.8, vec3(0.70, 0.70, 0.75));
        ink(col, sdSeg(k, vec2(15.0, 0.0), vec2(-15.0, 27.0)) - 0.8, vec3(0.70, 0.70, 0.75));
        ink(col, sdBox(k - vec2(0.0, 29.0), vec2(20.0, 3.5)), vec3(0.20, 0.20, 0.23));
    } else if (kind < 1.5) {
        ink(col, sdSeg(k, vec2(-22.0, 0.0), vec2(-22.0, 22.0)) - 1.2, vec3(0.05));
        ink(col, sdSeg(k, vec2(22.0, 0.0), vec2(22.0, 22.0)) - 1.2, vec3(0.05));
        ink(col, sdSeg(k, vec2(-26.0, 38.0), vec2(24.0, 54.0)) - 1.0, vec3(0.06));   // raised lid
        ink(col, sdRoundBox(k - vec2(0.0, 29.0), vec2(28.0, 6.0), 2.0), vec3(0.06, 0.06, 0.08));
        if (abs(k.y - 34.5) < 0.6 && abs(k.x) < 27.0) col = vec3(0.30);
    } else if (kind < 2.5) {
        ink(col, sdRoundBox(k - vec2(0.0, 22.0), vec2(24.0, 22.0), 2.0), vec3(0.38, 0.20, 0.10));
        ink(col, sdBox(k - vec2(0.0, 33.0), vec2(22.0, 3.0)), vec3(0.30, 0.15, 0.08));
        paint(col, sdBox(k - vec2(0.0, 16.0), vec2(18.0, 8.0)), vec3(0.32, 0.17, 0.08));
    } else {
        // DJ table with two turntables and a mixer
        ink(col, sdBox(k - vec2(0.0, 14.0), vec2(34.0, 14.0)), vec3(0.08, 0.08, 0.10));
        if (abs(k.y - 20.0) < 0.6 && abs(k.x) < 33.0) col = uAccent;
        for (int s2 = -1; s2 <= 1; s2 += 2) {
            vec2 t = k - vec2(float(s2) * 17.0, 30.0);
            ink(col, sdEllipse(t, vec2(12.0, 3.0)), vec3(0.20));
            paint(col, sdEllipse(t, vec2(9.0, 2.2)), vec3(0.05));
            float spin = fract(uBeats * 0.5 * (s2 > 0 ? 1.0 : -1.0));
            if (sdEllipse(t, vec2(9.0, 2.2)) < 0.0 && abs(fract(atan(t.y * 4.0, t.x) / 6.2831 + spin) - 0.5) < 0.04) col = vec3(0.55);
            paint(col, sdEllipse(t, vec2(1.5, 0.6)), uPrimary);
        }
        ink(col, sdBox(k - vec2(0.0, 30.0), vec2(4.0, 2.5)), vec3(0.15));
        for (int j = 0; j < 3; j++) if (sdCircle(k - vec2(-2.0 + float(j) * 2.0, 30.5), 0.6) < 0.0)
            col = bandAt(float(j) * 0.3 + 0.1) * pKe > 0.3 ? uAccent : vec3(0.3);
        return;
    }
    // keys: lit by pitch range
    vec2 kq = k - vec2(0.0, kind < 1.5 && kind > 0.5 ? 34.0 : (kind > 1.5 ? 36.0 : 30.0));
    if (abs(kq.x) < 18.0 && abs(kq.y) < 2.0) {
        float wk = floor((kq.x + 18.0) / 3.0);
        int ki = int(clamp(wk * 9.0 / 12.0, 0.0, 8.0));
        col = uKeys[ki] * pKe * uActive > 0.35 ? mix(uAccent, vec3(1.0), 0.3) : vec3(0.97, 0.96, 0.92);
        if (mod(kq.x + 18.0, 3.0) < 1.0) col = vec3(0.55, 0.55, 0.58);
        float m7 = mod(wk, 7.0);
        bool hasBlack = m7 == 0.0 || m7 == 1.0 || m7 == 3.0 || m7 == 4.0 || m7 == 5.0;
        if (kq.y > 0.0 && hasBlack && mod(kq.x + 18.0, 3.0) > 2.0) col = INK;
    }
}

// Drum-slot instruments around the riser (k = kit-local, riser top at 0).
// kind: 0 kit, 1 timpani, 2 pad controller
void drumInstrument(inout vec3 col, vec2 k, float kind, float kick, float pDr, vec3 shellCol, vec3 metal) {
    if (kind > 1.5) {
        ink(col, sdBox(k - vec2(0.0, 12.0), vec2(26.0, 12.0)), vec3(0.08, 0.08, 0.10));
        ink(col, sdBox(k - vec2(0.0, 25.0), vec2(16.0, 2.0)), vec3(0.18));
        for (int j = 0; j < 4; j++) {
            vec2 pc = k - vec2(-10.5 + float(j) * 7.0, 25.5);
            float lit = (j % 2 == 0 ? uHits.x : uHits.y) * pDr;
            paint(col, sdBox(pc, vec2(2.6, 1.0)), lit > 0.3 ? uAccent : vec3(0.35));
        }
        return;
    }
    if (kind > 0.5) {
        for (int s2 = -1; s2 <= 1; s2 += 2) {
            vec2 t = k - vec2(float(s2) * 18.0, 12.0);
            float bowl = max(sdEllipse(t, vec2(14.0, 12.0)), t.y);
            ink(col, bowl, vec3(0.78, 0.46, 0.22));
            ink(col, sdEllipse(t, vec2(14.0, 2.5 + floor((s2 < 0 ? uHits.x : uHits.y) * pDr))), vec3(0.95, 0.92, 0.82));
            ink(col, sdSeg(t, vec2(-8.0, -11.0), vec2(-10.0, -12.0)) - 0.6, vec3(0.3));
        }
        return;
    }
    for (int s2 = -1; s2 <= 1; s2 += 2) {
        float sx = float(s2) * 30.0;
        if (abs(k.x - sx) < 0.6 && k.y > -4.0 && k.y < 38.0) col = vec3(0.70, 0.70, 0.75);
    }
    float rideTilt = 0.25 + uHits.z * pDr * 0.15;
    ink(col, sdEllipse(k - vec2(-30.0, 33.0), vec2(10.0, 2.0)), metal);
    ink(col, sdEllipse(rot(rideTilt) * (k - vec2(30.0, 38.0)), vec2(12.0, 2.0)), metal);
    ink(col, sdBox(k - vec2(-16.0, 19.0), vec2(8.0, 3.0)), vec3(0.62, 0.62, 0.68));
    ink(col, sdEllipse(k - vec2(-16.0, 22.0), vec2(8.0, 2.5)), vec3(0.92, 0.92, 0.95));
    ink(col, sdBox(k - vec2(13.0, 25.0), vec2(7.0, 3.5)), shellCol);
    ink(col, sdEllipse(k - vec2(13.0, 28.5), vec2(7.0, 2.2)), vec3(0.92, 0.88, 0.80));
    float br = 15.0 + floor(kick * pDr * 1.5);
    ink(col, sdCircle(k - vec2(0.0, 10.0), br), shellCol);
    paint(col, sdCircle(k - vec2(0.0, 10.0), br - 2.5), vec3(0.96, 0.92, 0.82));
    ink(col, sdCircle(k - vec2(0.0, 10.0), 4.0), metal);
}

void main() {
    float scale = max(1.0, floor(min(uResolution.y / VH, uResolution.x / 440.0)));
    vec2 v = floor((gl_FragCoord.xy - 0.5 * uResolution) / scale) + 0.5;
    // When the width limits the scale there's spare height: keep the stage
    // front near the bottom and give the extra room to the curtain above.
    v.y += max(0.0, floor(uResolution.y / scale * 0.5 - 108.0));

    // A follow-spot: the stage light follows the cursor, and the
    // performers it lands on are lit. The pointer is in the shader's own
    // coordinates and the stage is in pixel-art ones, so it is converted
    // on the way: the same arithmetic v was built with, applied to the
    // pointer instead of to this pixel.
    vec2 mp = vec2(floor(pointerAt().x * uResolution.y / scale) + 0.5,
                   floor(pointerAt().y * uResolution.y / scale) + 0.5
                   + max(0.0, floor(uResolution.y / scale * 0.5 - 108.0)));
    float lamp = exp(-dot(v - mp, v - mp) / 4200.0) * uMouseHere;

    // uPlay: singers and guitarists play as they walk; drums and keys at their instrument.
    float pDr = uPlay[0], pBa = uPlay[1], pGu = uPlay[2], pKe = uPlay[3];
    float beat = uBeats;
    float ph = fract(beat);
    float ph8 = fract(beat * 2.0);
    bool snareBeat = mod(floor(beat), 2.0) > 0.5;
    bool kickBeat = !snareBeat;
    float kick = max(kickBeat ? exp(-ph * 7.0) : 0.0, uHits.x) * uActive;

    // Room
    vec3 col = pickRoom(v);

    // Beat strobe on top of the room for electronic / metal
    strobeOverlay(v, col);

    vec3 roomCol = col;
    int G = uGenre;
    float accent = uHits.x * step(0.6, uHits.x);

    // Instruments per genre for the drum / keys / guitar / bass slots.
    float drumKind = G == 0 ? 1.0 : ((G == 1 || G == 4) ? 2.0 : 0.0);          // kit, timpani, pads
    float keysKind = G == 0 ? 1.0 : ((G == 5 || G == 6 || G == 7) ? 2.0 : ((G == 1 || G == 4) ? 3.0 : 0.0));
    bool violinSlot = G == 0, saxSlot = G == 5;
    bool cello = G == 0, upright = G == 5 || G == 7;
    vec3 shell = G == 2 ? vec3(0.75, 0.10, 0.15) : mix(uPrimary, vec3(0.1), 0.35);
    vec3 metal = vec3(0.95, 0.78, 0.32);

    // --- drums: rise up from behind the kit, sink back at the end
    {
        Outfit o = outfit(G, 0);
        vec2 d = v - vec2(0.0, -26.0 - floor(uMX[0]));
        bool aboveRiser = v.y > -21.5;
        if (aboveRiser) {
            person(col, d, SKIN_B, o.hair, o.shirt, o.pants, o.style, 0.0, false, false, false, 0.0, -1.0, 0.0);
            outfitExtras(col, d, o);
        }
        // Gear only for slots in this genre's line-up, or someone who's actually out.
        bool gear = (uLineupMask & 1) != 0 || uPresent[0] > 0.5;
        if (G != 9 && gear) {
            ink(col, sdBox(v - vec2(0.0, -30.0), vec2(52.0, 9.0)), mix(uSecondary, vec3(0.30, 0.18, 0.10), 0.5));
            if (abs(v.y + 21.5) < 1.0 && abs(v.x) < 51.0) col = mix(uAccent, vec3(0.7, 0.46, 0.26), 0.5);
        }
        if (gear) drumInstrument(col, v - vec2(0.0, -26.0), drumKind, kick, pDr, shell, metal);
        if (aboveRiser) {
            float lUp = snareBeat ? swing(ph) : 1.0 - smoothstep(0.7, 1.0, ph);
            float rUp = swing(ph8);
            vec2 lh, rh;
            if (drumKind > 1.5) {          // tapping pads
                lh = mix(vec2(-10.0, 22.0), vec2(-8.0, 26.0 + (1.0 - uHits.x) * 3.0), pDr);
                rh = mix(vec2(10.0, 22.0), vec2(8.0, 26.0 + (1.0 - uHits.y) * 3.0), pDr);
            } else if (drumKind > 0.5) {   // timpani mallets, alternating
                lh = mix(vec2(-12.0, 24.0), vec2(-18.0, 16.0 + lUp * 8.0), pDr);
                rh = mix(vec2(12.0, 24.0), vec2(18.0, 16.0 + rUp * 8.0), pDr);
            } else {
                lh = mix(vec2(-12.0, 24.0), vec2(-15.0, 25.0 + lUp * 9.0), pDr);
                rh = mix(vec2(12.0, 24.0), vec2(24.0, 41.0 + rUp * 7.0), pDr);
            }
            arm(col, d, vec2(-9.0, 34.0), lh, -1.0, o.shirt, SKIN_B);
            arm(col, d, vec2(9.0, 34.0), rh, 1.0, o.shirt, SKIN_B);
            if (drumKind < 1.5) {
                vec3 stick = drumKind > 0.5 ? vec3(0.95) : vec3(0.90, 0.80, 0.55);
                ink(col, sdSeg(d, lh, lh + vec2(-5.0, -4.0 + lUp * 2.0)) - 0.6, stick);
                ink(col, sdSeg(d, rh, rh + vec2(6.0, -3.0 + rUp * 2.0)) - 0.6, stick);
            }
        }
    }

    // --- keys (stage right): player a step upstage of the instrument
    {
        Outfit o = outfit(G, 3);
        float walk = uWalk[3];
        float bob = walk >= 0.0 ? floor(abs(sin(walk)) * 1.5) : 0.0;
        vec2 k = v - vec2(uMX[3], -60.0 + floor(swing(ph) * pKe) + bob);
        vec2 kb = v - vec2(uHome[3], -76.0);
        musician(col, k, o, SKIN_A, 0.0, false, 0.0, walk, uFace[3]);
        if ((uLineupMask & 8) != 0 || uPresent[3] > 0.5) keysInstrument(col, kb, keysKind, pKe);
        int loud = 0; float best = -1.0; int loudR = 4; float bestR = -1.0;
        for (int i = 0; i < 9; i++) {
            if (i < 5 && uKeys[i] > best) { best = uKeys[i]; loud = i; }
            if (i >= 4 && uKeys[i] > bestR) { bestR = uKeys[i]; loudR = i; }
        }
        float keysY = (keysKind > 2.5 ? 30.0 : (keysKind > 0.5 && keysKind < 1.5 ? 34.0 : (keysKind > 1.5 ? 36.0 : 30.0))) - 16.0;
        float lx = -18.0 + (float(loud) + 0.5) / 9.0 * 36.0;
        float rx = -18.0 + (float(loudR) + 0.5) / 9.0 * 36.0;
        if (keysKind > 2.5) { lx = -17.0 + sin(uBeats * PI * 2.0) * 3.0 * pKe; rx = 2.0; }   // scratch + fader
        float dip = swing(ph8) * 1.5;
        arm(col, k, vec2(-9.0, 36.0), mix(vec2(-11.0, 23.0), vec2(lx, keysY + dip), pKe), -1.0, o.shirt, SKIN_A);
        arm(col, k, vec2(9.0, 36.0), mix(vec2(11.0, 23.0), vec2(rx, keysY + 1.5 - dip), pKe), 1.0, o.shirt, SKIN_A);
    }

    // --- bass slot (stage left): electric bass, or cello / upright bass
    {
        Outfit o = outfit(G, 1);
        float walk = uWalk[1];
        float bob = walk >= 0.0 ? floor(abs(sin(walk)) * 1.5) : 0.0;
        float sway = floor(sin(beat * PI * 0.25) * 2.5 * pBa);
        vec2 b = v - vec2(uMX[1] + sway, -70.0 + bob);
        float tap = floor((1.0 - swing(ph)) * 2.0 * pBa);
        musician(col, b, o, SKIN_C, 0.0, false, tap, walk, uFace[1]);
        if (cello || upright) {
            bigString(col, b, cello, cello ? swing(ph) * pBa : (1.0 - swing(ph)) * pBa, SKIN_C, o.shirt);
        } else {
            float lean = floor(sin(beat * PI * 0.25 + 1.0) * 2.0 * pBa);
            b.x -= lean * clamp((b.y - 18.0) / 30.0, 0.0, 1.0);
            vec2 at = vec2(2.0, 24.0);
            float ang = (0.10 * sin(beat * PI * 0.25) - 0.15 * accent) * pBa;
            vec3 body = G == 2 ? vec3(0.85, 0.15, 0.15) : mix(uPrimary, vec3(0.9), 0.15);
            guitar(col, b, at, ang, -1.0, 34.0, body, body * 0.6);
            float pl = max(1.0 - swing(ph), uPluck) * pBa;
            arm(col, b, vec2(-9.0, 36.0), onGuitar(at, ang, vec2(-24.0, 13.0)), -1.0, o.shirt, SKIN_C);
            arm(col, b, vec2(9.0, 36.0), onGuitar(at, ang, vec2(2.0 - pl * 2.0, -2.0 - pl * 2.0)), 1.0, o.shirt, SKIN_C);
        }
    }

    // --- guitar slot (left of centre, played by a woman): guitar, violin or sax
    {
        Outfit o = outfit(G, 2);
        float walk = uWalk[2];
        float bob = walk >= 0.0 ? floor(abs(sin(walk)) * 1.5) : 0.0;
        float sway = floor(sin(beat * PI * 0.25 + 2.0) * 2.5 * pGu);
        vec2 g = v - vec2(uMX[2] + sway, -70.0 + bob);
        float tap = floor((1.0 - swing(ph)) * 2.0 * pGu);
        musician(col, g, o, SKIN_A, 0.0, true, tap, walk, uFace[2]);
        if (violinSlot) {
            violin(col, g, swing(ph) * pGu, SKIN_A, o.shirt);
        } else if (saxSlot) {
            sax(col, g, floor(swing(ph8) * 2.0 * pGu), SKIN_A, o.shirt);
        } else {
            float lean = floor(sin(beat * PI * 0.25 + 3.0) * 2.5 * pGu);
            g.x -= lean * clamp((g.y - 18.0) / 30.0, 0.0, 1.0);
            vec2 at = vec2(-2.0, 24.0);
            float ang = (-0.10 * sin(beat * PI * 0.25 + 2.0) - 0.2 * accent) * pGu;
            vec3 inner = vec3(1.0, 0.75, 0.25), outer = vec3(0.55, 0.15, 0.08);   // rock sunburst
            if (G == 7) { inner = vec3(0.90, 0.68, 0.38); outer = vec3(0.62, 0.40, 0.18); }        // acoustic
            else if (G == 6) { inner = vec3(0.85, 0.12, 0.10); outer = vec3(0.55, 0.05, 0.05); }   // blues red
            else if (G == 3) { inner = vec3(1.0, 0.85, 0.92); outer = vec3(0.95, 0.45, 0.70); }    // pop pink
            else if (G == 8) { inner = vec3(1.0, 0.88, 0.45); outer = vec3(0.80, 0.60, 0.20); }    // R&B gold
            else if (G != 2) { inner = mix(uPrimary, vec3(1.0), 0.3); outer = uSecondary; }
            guitar(col, g, at, ang, 1.0, 30.0, inner, outer);
            float strum = (swing(ph8) - 0.5) * (0.7 + uHits.w * 0.6) * pGu;
            float fret = floor(mod(beat / 4.0, 3.0)) * 3.0 * pGu;
            arm(col, g, vec2(9.0, 36.0), onGuitar(at, ang, vec2(18.0 + fret, 11.0 + fret * 0.6)), 1.0, o.shirt, SKIN_A);
            arm(col, g, vec2(-9.0, 36.0), onGuitar(at, ang, vec2(-1.0, 1.0 + strum * 8.0)), -1.0, o.shirt, SKIN_A);
        }
    }

    // --- singers, front and centre with handheld mics
    {
        float voc = clamp(uVocal * 2.5, 0.0, 1.0) * uActive;
        for (int i = 4; i < 6; i++) {
            float p = uPlay[i];
            float sway = floor(sin(beat * PI * 0.5) * 2.0 * p * uActive) * (i == 4 ? 1.0 : -1.0);
            singer(col, v, i, i == 4, voc * p, sway);
        }
    }

    // Ambient has no stage: anyone performing is a dark silhouette
    // against the colour field, rim-lit in the accent colour.
    if (G == 9 && any(notEqual(col, roomCol))) {
        col = mix(vec3(0.02, 0.015, 0.04), uAccent, 0.12) + uAccent * 0.08 * (1.0 + uBass);
    }

    // The follow-spot over the top, so the performers it lands on are
    // lit and not just the floor behind them.
    col += vec3(1.0, 0.95, 0.78) * lamp * 0.55;

    fragColor = vec4(col, 1.0);
}

// Hit curve for a stick or strum: down at phase 0, back up by 0.5,
// starting down again from 0.7.
float swing(float ph) { return ph < 0.5 ? smoothstep(0.0, 0.5, ph) : 1.0 - smoothstep(0.7, 1.0, ph); }

void arm(inout vec3 col, vec2 p, vec2 s, vec2 h, float side, vec3 sleeve, vec3 skin) {
    vec2 e = (s + h) * 0.5 + vec2(side * 3.0, -2.0);
    ink(col, sdSeg(p, s, e) - 2.6, sleeve);
    ink(col, sdSeg(p, e, h) - 2.0, skin);
    ink(col, sdCircle(p - h, 2.6), skin);
}

// Musician with feet at the origin, ~60px tall.
// style: 0 spiky, 1 afro, 2 side-parted, 3 long, 4 beanie.
// female adds eyelashes and lips; dress swaps trousers for a skirt
// in the shirt colour; tap lifts the right foot.
// walk >= 0 steps the legs; look shifts the face toward the walking direction.
void person(inout vec3 col, vec2 p, vec3 skin, vec3 hair, vec3 shirt, vec3 pants,
            float style, float mouth, bool legs, bool female, bool dress, float tap,
            float walk, float look) {
    vec2 h = p - vec2(0.0, 50.0);
    if (style > 0.5 && style < 1.5) ink(col, sdCircle(h - vec2(0.0, 3.0), 12.0), hair);
    if (style > 2.5 && style < 3.5) ink(col, sdRoundBox(h - vec2(0.0, -7.0), vec2(10.0, 14.0), 5.0), hair);
    if (legs) {
        float liftL = walk >= 0.0 ? floor(max(0.0, sin(walk)) * 3.0) : 0.0;
        float liftR = walk >= 0.0 ? floor(max(0.0, -sin(walk)) * 3.0) : tap;
        vec2 lp = vec2(abs(p.x), p.y - (p.x > 0.0 ? liftR : liftL));
        if (dress) ink(col, sdBox(lp - vec2(4.0, 9.0), vec2(2.2, 8.0)), skin * 0.92);
        else ink(col, sdBox(lp - vec2(4.5, 11.0), vec2(3.5, 9.0)), lit(pants, p.x, 4.0));
        ink(col, sdEllipse(lp - vec2(5.5, 2.0), vec2(5.0, 2.5)), dress ? vec3(0.55, 0.10, 0.18) : vec3(0.28, 0.16, 0.10));
        if (dress) ink(col, max(abs(p.x) - (9.0 + (22.0 - p.y) * 0.45), abs(p.y - 15.0) - 7.0), lit(shirt, p.x, 4.0));
    }
    ink(col, sdRoundBox(p - vec2(0.0, 29.0), vec2(10.0, 11.0), 4.0), lit(shirt, p.x, 4.0));
    paint(col, sdBox(p - vec2(0.0, 40.5), vec2(3.0, 2.0)), skin * 0.82);
    ink(col, sdCircle(vec2(abs(h.x) - 8.0, h.y + 1.0), 2.0), skin * 0.9);
    float hd = sdEllipse(h, vec2(8.0, 9.0));
    ink(col, hd, lit(skin, h.x, 4.5));
    if (hd < 0.0) {
        bool hr;
        if (style < 0.5)      hr = h.y > 3.0 - abs(fract(h.x / 4.0) - 0.5) * 4.0;
        else if (style < 1.5) hr = h.y > 2.0;
        else if (style < 2.5) hr = h.y > 4.0 || (h.y > -1.0 && h.x < -4.5);
        else if (style < 3.5) hr = h.y > 3.0 || abs(h.x) > 6.0;
        else                  hr = h.y > 3.0;
        if (hr) col = style > 3.5 ? vec3(0.85, 0.22, 0.20) : lit(hair, h.x, 4.0);
    }
    if (style > 3.5) {
        paint(col, sdBox(h - vec2(0.0, 3.5), vec2(8.5, 1.5)), vec3(0.62, 0.12, 0.12));
        ink(col, sdCircle(h - vec2(0.0, 10.5), 2.5), vec3(0.95, 0.93, 0.88));
    }
    vec2 f = h - vec2(floor(look * 2.0), 0.0);    // face features turn with the walk
    paint(col, sdBox(vec2(abs(f.x) - 3.0, f.y), vec2(0.6, 1.2)), INK);
    if (female) paint(col, sdBox(vec2(abs(f.x) - 4.2, f.y - 1.6), vec2(0.6, 0.5)), INK);
    paint(col, sdBox(vec2(abs(f.x) - 4.0, f.y + 3.5), vec2(1.2, 0.6)), mix(skin, vec3(1.0, 0.45, 0.45), 0.45));
    if (mouth > 0.08) ink(col, sdEllipse(f - vec2(0.0, -5.0), vec2(1.8, 0.6 + mouth * 2.2)), vec3(0.55, 0.08, 0.14));
    else paint(col, sdBox(f - vec2(0.0, -5.0), vec2(1.5, 0.4)), female ? vec3(0.80, 0.22, 0.32) : INK);
}

// Guitar-shaped body + neck. dir = +1 neck to the right, -1 to the left.
// Guitar-local point to world, for a guitar at `at` tilted by `ang`.
vec2 onGuitar(vec2 at, float ang, vec2 local) { return at + rot(-ang) * local; }

void guitar(inout vec3 col, vec2 p, vec2 at, float ang, float dir, float neckLen, vec3 inner, vec3 outer) {
    vec2 q = rot(ang) * (p - at);
    vec2 tip = vec2(dir * neckLen, neckLen * 0.62);
    ink(col, sdSeg(q, vec2(dir * 4.0, 3.0), tip) - 1.6, vec3(0.45, 0.27, 0.12));
    ink(col, sdBox(q - tip - vec2(dir * 2.0, 1.0), vec2(2.5, 1.6)), vec3(0.30, 0.17, 0.08));
    float body = min(sdCircle(q - vec2(-dir * 2.0, -2.0), 8.5), sdCircle(q - vec2(dir * 3.0, 3.0), 6.5));
    float r = length(q - vec2(-dir * 1.0, 0.0));
    vec3 burst = r < 4.0 ? inner : (r < 7.0 ? mix(inner, outer, 0.5) : outer);
    ink(col, body, burst);
    paint(col, sdCircle(q - vec2(dir * 1.5, 1.0), 2.2), INK);
    if (body < 0.0 && sdSeg(q, vec2(-dir * 6.0, -3.0), tip) < 0.5) col = vec3(0.85, 0.85, 0.8);
}

// Lead singer at the front edge of the stage, carrying a handheld mic
// (they sing as they walk on). side = -1 mirrors them.
void singer(inout vec3 col, vec2 v, int i, bool female, float voc, float sway) {
    float home = uHome[i], x = uMX[i], walk = uWalk[i];
    float side = home < -1.0 ? -1.0 : 1.0;
    float bob = walk >= 0.0 ? floor(abs(sin(walk)) * 1.5) : 0.0;
    vec2 s = v - vec2(x + sway, -84.0 + bob);
    s.x *= side;
    vec3 skin = female ? SKIN_A : SKIN_B;
    Outfit o = outfit(uGenre, i);
    if (female) o.style = 3.0;
    person(col, s, skin, o.hair, o.shirt, o.pants, o.style, voc, true, female, female, 0.0, walk, uFace[i] * side);
    if (!female && uGenre == 2) paint(col, sdBox(s - vec2(0.0, 32.0), vec2(2.5, 7.0)), vec3(0.95, 0.95, 0.92));   // rock: white tee under the jacket
    outfitExtras(col, s, o);
    vec3 sleeve = o.shirt;
    // mic hand up at the mouth; the free arm rises with the vocals
    vec2 hand = vec2(7.0, 40.0);
    arm(col, s, vec2(9.0, 36.0), hand, 1.0, sleeve, skin);
    ink(col, sdSeg(s, hand, hand + vec2(-2.0, 4.0)) - 1.2, vec3(0.18, 0.18, 0.20));
    ink(col, sdCircle(s - hand - vec2(-2.5, 5.5), 2.4), vec3(0.62, 0.62, 0.66));
    arm(col, s, vec2(-9.0, 36.0), mix(vec2(-12.0, 22.0), vec2(-19.0, 52.0), voc), -1.0, sleeve, skin);
}
