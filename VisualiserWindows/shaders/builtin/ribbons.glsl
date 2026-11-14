// Glass polyhedra as pairs of parallel planes: the first 4 axes make an
// octahedron, all 10 an icosahedron (face normals, unit length).
const vec3 kFaceAxes[10] = vec3[10](
    vec3(0.5774, 0.5774, 0.5774), vec3(0.5774, 0.5774, -0.5774),
    vec3(0.5774, -0.5774, 0.5774), vec3(-0.5774, 0.5774, 0.5774),
    vec3(0.0, 0.3568, 0.9342), vec3(0.0, 0.3568, -0.9342),
    vec3(0.3568, 0.9342, 0.0), vec3(-0.3568, 0.9342, 0.0),
    vec3(0.9342, 0.0, 0.3568), vec3(-0.9342, 0.0, 0.3568));

const float kFloorY = -0.62;
const int kGems = 8;
const int kStrands = 18;

// The film's palette: hot pink and electric cyan light over a dark navy room.
const vec3 ribbonPink = vec3(1.0, 0.12, 0.40);
const vec3 ribbonDeep = vec3(0.50, 0.0, 0.24);
const vec3 ribbonCyan = vec3(0.0, 1.0, 0.72);
const vec3 ribbonTeal = vec3(0.0, 0.55, 0.45);
const vec3 glassCyan = vec3(0.0, 1.0, 0.64);

// Spectrum at x (0..1), interpolated between bands so it has no steps.
float ribbonSpec(float x01) {
    float f = clamp(x01, 0.0, 1.0) * 95.0;
    int i = int(floor(f));
    float t = fract(f);
    return mix(uBands[i], uBands[min(i + 1, 95)], t * t * (3.0 - 2.0 * t));
}

// How far the whole scene has slid to the left, in world units. A slow base
// drift on the wall clock (so it never stops, even in silence) plus uScroll,
// so it speeds up with the music. Both only ever increase. Because the world
// itself moves, perspective gives the parallax: near glass and near stretches
// of ribbon cross the screen faster than far ones.
float sceneShift() { return uClock * 0.15 + uScroll * 0.85; }

// The world turns with the beat: half at a steady pace on the beat clock and
// half in an eased step per beat, so it surges a little on each beat but
// never stops or snaps. Forward-only, so it never runs backwards.
float beatTurn() { return 0.5 * uBeats + 0.5 * (floor(uBeats) + smoothstep(0.0, 1.0, fract(uBeats))); }

// A soft swell on every beat, in place of the raw bass and beat-strength
// inputs (which jump from one frame to the next): it eases in over about a
// third of a beat and dies away over the next beat or two, and the swells
// of the last beats overlap, so it rises and falls like breathing. Zero in
// silence.
float swellAt(float x) { return smoothstep(0.0, 0.32, x) * exp(-x * 1.4); }
float beatSwell() {
    float ph = fract(uBeats);
    return (swellAt(ph) + swellAt(ph + 1.0) + swellAt(ph + 2.0) + swellAt(ph + 3.0)) * 1.2 * uActive;
}
float gSwell;   // beatSwell(), worked out once per pixel at the top of main()
float gSpec;    // the low end of the spectrum, worked out once per pixel

// Silence: with no music the glass fades away and the four ribbons gather
// into one still set of ten perfectly level lines, 2 px apart, across the
// middle of the screen. gPlay is how much music there is (0 in silence, 1
// playing), eased on the slow loudness so it unfurls over about half a
// second when the music starts and settles over a second or two when it
// stops, with no chattering in quiet passages. At 1 everything is exactly
// as when playing. gGlass is how visible the glass is: it goes first and
// comes back last.
float gPlay;
float gGlass;
float gCut = 1e9;   // ribbons further along the ray than this (behind fading glass) ...
float gBehind = 1.0; // ... show this much
// How hard the cyan pulses hit: from the eased bass and loudness, so a loud
// kick sends a big, bright, long cyan surge racing through and a quiet
// passage only a faint, short one, without flicker. 0..1.
float gDrive;
float pulseDrive() { return smoothstep(0.1, 0.27, uBassSmooth * 0.6 + uLevelSmooth * 1.6); }
float gPx;      // one screen pixel, in world units, on the plane the lines settle on
const float kIdleZ = 0.1;                            // the lines settle at the hero's depth...
const float kIdleY = -0.40 + 0.24 * (kIdleZ + 3.3) / 1.3;   // ... at the height of the screen's middle
float playAmount() {
    float m = uLevelSmooth * (0.3 + 0.7 * uActive);  // falls quickly once the sound has gone
    float x = smoothstep(0.004, 0.035, m);
    return x * x * (3.0 - 2.0 * x);
}
// The ten lines: 1 px tall, on whole pixel rows, 2 px apart, centred.
float idleRow0() { return floor(uResolution.y * 0.5) - 8.5; }
float idleLines() {
    float d = gl_FragCoord.y - idleRow0();
    float k = clamp(floor(d * 0.5 + 0.5), 0.0, 9.0);
    return clamp(1.0 - abs(d - 2.0 * k), 0.0, 1.0);
}

// The ribbons, from one big hero sheaf down to thin threads, each at its own
// height and depth with its own slow undulation. Some flow left with the
// scene, some flow right against it, and their pulses race the way they flow.
//  x: base height  y: wave height  z: half-width of the sheaf  w: strands
const vec4 kRibShape[4] = vec4[4](
    vec4(0.08, 0.32, 0.24, 18.0),
    vec4(0.95, 0.34, 0.145, 12.0),
    vec4(-0.26, 0.24, 0.014, 4.0),
    vec4(1.9, 0.45, 0.07, 7.0));
//  x: strand radius  y: flow (+ left, - right, and how fast)  z: depth  w: depth swing
const vec4 kRibFlow[4] = vec4[4](
    vec4(0.036, 1.0, 0.1, 0.65),
    vec4(0.028, -0.8, 1.55, 0.5),
    vec4(0.006, 1.3, -1.05, 0.3),
    vec4(0.018, -0.6, 2.7, 0.4));
//  x, y: wave frequencies  z: phase  w: beat offset of its pulses
const vec4 kRibWave[4] = vec4[4](
    vec4(1.2, 2.28, 0.0, 0.0),
    vec4(0.85, 1.85, 1.7, 0.37),
    vec4(1.55, 2.9, 3.1, 0.61),
    vec4(0.75, 1.6, 4.4, 0.83));

// How far ribbon k's pattern has slid along itself.
float ribbonShift(int k) { return sceneShift() * kRibFlow[k].y; }
// Where ribbon k rests, as the music fades: its height and depth move to the
// lines' and its depth swing flattens out.
float ribbonBaseY(int k) { return mix(kIdleY, kRibShape[k].x, gPlay); }
float ribbonBaseZ(int k) { return mix(kIdleZ, kRibFlow[k].z, gPlay); }

// How cyan the ribbon is at x (position across its sheet, measured the way it
// flows) for a strand that lights `delay` beats late: every beat launches a
// band of bright cyan that races along the strands, born off screen, easing
// in and fading out again over the next beat and a half, with a soft head
// (`sharp`) and a long soft tail, so the ribbon flows back to pink without a
// snap. Each ribbon's bands set off at their own point in the beat (`off`),
// so they never all fire at once. In silence there are none.
float pulseAmp(float ph) { return smoothstep(0.0, 0.2, ph) * (1.0 - smoothstep(0.45, 1.5, ph)); }
float ribbonPulse(float x, float off, float delay, float spd, float energy, float sharp) {
    float ph = fract(uBeats - off) - delay;
    float s0 = x - 4.4 + spd * ph;                      // behind this beat's front
    float s1 = s0 + spd;                                // ... and the last beat's
    float tail = 1.2 / mix(0.55, 1.7, gDrive);           // louder: a longer tail
    float p0 = exp(-max(s0 - 1.1, 0.0) * tail - max(-s0, 0.0) * sharp) * pulseAmp(ph);
    float p1 = exp(-max(s1 - 1.1, 0.0) * tail - max(-s1, 0.0) * sharp) * pulseAmp(ph + 1.0);
    return max(p0, p1) * energy;
}

// Light from ribbon k at point q on its plane: a braided sheaf of fine
// glowing strands, hot pink, with cyan pulses racing through it on the beat.
// soft > 0 blurs it, for reflections in the floor.
vec3 ribbonLight(vec2 q, float soft, int k) {
    vec4 shp = kRibShape[k], flw = kRibFlow[k], wav = kRibWave[k];
    float dir = sign(flw.y);
    float size = shp.z / 0.3;                            // 1 for the hero sheaf
    float turn = beatTurn();
    float X = q.x + ribbonShift(k);                      // position along the ribbon
    float xf = q.x * dir;                                // ... measured the way it flows
    float edge = smoothstep(-4.8, -3.6, q.x) * smoothstep(4.8, 3.6, q.x);
    // Broad waves; the beat swell bends the sheaf a little harder.
    float amp = shp.y * (1.0 + (0.14 * gSpec + 0.55 * gSwell) * size) * mix(0.75, 1.0, uActive) * gPlay;
    float ph = turn * 0.45 + wav.z;
    float s = uScroll * 0.3 * dir;                       // a gentle ripple along the ribbon on top of the slide
    float a1 = X * wav.x + s + ph;
    float a2 = X * wav.y - s * 0.6 + ph * 1.7;
    float yc = ribbonBaseY(k) + amp * sin(a1) + amp * 0.35 * sin(a2);
    float lo = kFloorY + shp.z * 0.6 + 0.06;             // skim the floor rather than sink into it
    yc = mix(yc, 0.5 * (yc + lo + sqrt((yc - lo) * (yc - lo) + 0.004)), gPlay);
    float slope = amp * (wav.x * cos(a1) + 0.35 * wav.y * cos(a2));
    float norm = inversesqrt(1.0 + slope * slope);
    float dc = abs(q.y - yc) * norm;
    // Far from a small ribbon there's nothing left of its glow to draw.
    if (k > 0 && dc > 1.4 * sqrt(size) + soft * 24.0) return vec3(0.0);
    // The sheaf twists as it travels: fanned wide where it faces you, gathered
    // where it turns, and each strand weaves a little on its own so they cross.
    float width = mix(10.0 * gPx, shp.z * (1.0 + 0.3 * gSwell) * (0.75 + 0.25 * sin(X * 0.6 + wav.z)), gPlay);
    float twist = X * 0.55 - uScroll * 0.25 * dir + turn * 0.2 + wav.z;
    float face = cos(twist);
    float sw = flw.x;                                    // strand radius: fine silk
    float energy = uActive * gPlay * mix(0.25, 1.25, gDrive);   // faint when quiet, a surge when loud
    const float spd = 7.5;                               // world units per beat
    vec3 bundle = vec3(0.0);
    float pc = smoothstep(0.15, 0.95, ribbonPulse(xf, wav.w, 0.06, spd, energy, 1.2));   // the wave through the sheaf as a whole
    vec3 hotC = mix(ribbonPink, ribbonCyan, pc);
    // Only pixels near the sheaf pay for its strands.
    if (dc < width * 1.6 + 0.12 * size + soft * 12.0) {
        vec3 strands = vec3(0.0);
        int n = int(shp.w);
        float fn = shp.w;
        // Per-strand phases advance by a fixed angle each strand (cheap rotations, not sin per strand).
        vec2 pw = vec2(sin(X * 0.8), cos(X * 0.8));      // thickness wobble, steps 1.3 rad per strand
        vec2 ps = vec2(sin(X * 0.9 - uScroll * 0.4), cos(X * 0.9 - uScroll * 0.4));   // shimmer, steps 2.3 rad
        const mat2 stepW = mat2(0.2675, -0.9636, 0.9636, 0.2675);
        const mat2 stepS = mat2(-0.6663, -0.7457, 0.7457, -0.6663);
        float aa = mix(max(soft, 0.5 * gPx), max(soft, 0.0025), gPlay);
        float lane = (idleRow0() - 0.5 * uResolution.y) * gPx;   // where strand lane 0 settles, from the middle
        for (int i = 0; i < kStrands; i++) {
            if (i >= n) break;
            float fi = float(i);
            float h1 = fract(fi * 0.6180), h2 = fract(fi * 0.7549 + 0.31), h3 = fract(fi * 0.5698 + 0.17);
            float u = fi / (fn - 1.0) * 2.0 - 1.0;
            float off = width * (u * (0.5 + 0.5 * abs(face)) + 0.45 * sin(X * (1.1 + 0.25 * h1) + fi * 2.4 + turn * 0.3));
            off = mix(lane + 2.0 * gPx * floor((u * 0.5 + 0.5) * 9.999), off, gPlay);   // in silence, onto one of the ten lines
            float sd = (q.y - yc - off) * norm;
            float d = abs(sd);
            float w = mix(0.5 * gPx, sw * (0.45 + 0.9 * h2) * (0.75 + 0.25 * pw.x), gPlay);
            float cov = 1.0 - smoothstep(w - aa, w + aa, d);     // the strand's outline, anti-aliased
            float x = min(d / (w + aa), 1.0);
            float tube = sqrt(1.0 - x * x);                       // round strand: hot in the middle, deep at the rim
            // each strand is itself a few hairline fibres, read where it's sharp enough
            float fib = mix(0.55 + 0.45 * cos(sd / (w + aa) * 9.4 + fi * 1.7), 1.0, max(smoothstep(0.3, 0.8, aa / w), 1.0 - gPlay));
            // some strands catch the wave before others, so it shimmers through the sheaf;
            // as a band fades, its strands drop back to pink one by one
            float pulse = smoothstep(0.08 + 0.55 * h2, 0.28 + 0.55 * h2, ribbonPulse(xf, wav.w, h3 * 0.14, spd, energy, 3.0));
            vec3 hot = mix(ribbonPink, ribbonCyan, pulse);
            vec3 deep = mix(ribbonDeep, ribbonTeal, pulse);
            float t2 = tube * tube, t8 = t2 * t2; t8 *= t8;
            float t32 = t8 * t8; t32 *= t32;
            // the hats glint on the pulse heads (a fine sparkle running along the strand)
            float glint = 1.0 + pulse * (0.45 * gDrive + 0.6 * uTreble * (0.5 + 0.5 * sin(X * 23.0 + fi * 2.1 - uScroll * 9.0)));
            vec3 c = mix(deep, hot, tube) * (0.35 + 1.35 * tube) * (1.0 - 0.25 * pulse) * glint * fib
                   + mix(vec3(1.0, 0.5, 0.75), vec3(0.3, 1.0, 1.0), pulse) * t32 * t8 * 0.6;
            strands = mix(strands, c * mix(1.0, 0.75 + 0.25 * ps.x, gPlay), cov * 0.8);   // later strands lie over earlier ones
            pw = stepW * pw;
            ps = stepS * ps;
        }
        // the strands' shared glow: bright through the sheaf, falling off outside it
        float halo = 5.0 * exp(-max(dc - width * 0.9, 0.0) / (0.06 * size + soft * 3.0));
        bundle = strands + (hotC * halo * 0.15
               + hotC * mix(vec3(1.0, 0.55, 0.8), vec3(0.3, 1.0, 0.95), pc) * exp(-dc / (width * 0.45 + 0.02 * size + soft * 4.0)) * 0.55) * gPlay;   // thick luminous core
    }
    // the glow the sheaf throws on the air around it
    bundle += hotC * exp(-dc / (0.2 * sqrt(size) + soft * 4.0)) * 0.08 * sqrt(size) * gPlay;
    float glow = mix(1.6, (0.45 + 0.55 * uActive) * (0.95 + 0.3 * gSwell), gPlay);
    return bundle * 1.1 * edge * glow / (1.0 + soft * 20.0);
}

// Each ribbon lives on its own curved sheet that slides with its flow, so
// stretches of ribbon swing towards the camera (and move faster) and back
// away again. Where a ray meets it (a few fixed-point steps), or -1.
float ribbonDepth(float x, int k) {
    return ribbonBaseZ(k) + kRibFlow[k].w * gPlay * sin((x + ribbonShift(k)) * 0.45 + 0.9 + kRibWave[k].z);
}
float ribbonT(vec3 ro, vec3 rd, int k) {
    if (abs(rd.z) < 1e-3) return -1.0;
    float t = (ribbonDepth(ro.x, k) - ro.z) / rd.z;
    int steps = k == 0 ? 4 : 2;                          // the small ones' sheets bend less
    for (int i = 0; i < 4; i++) {
        if (i >= steps) break;
        t = (ribbonDepth(ro.x + rd.x * max(t, 0.0), k) - ro.z) / rd.z;
    }
    return t;
}

// One ribbon as seen along a ray, or nothing if the ray doesn't reach it.
// Blur is soft + softT per unit of distance, never thinner than a pixel.
vec3 ribbonAlong(vec3 ro, vec3 rd, float soft, float softT, float tMax, int k) {
    if (k > 0) {
        // A cheap first look: rays that pass well above or below the band
        // this ribbon can wave through never reach its glow.
        float t0 = (ribbonBaseZ(k) - ro.z) / rd.z;
        float band = 2.6 * kRibShape[k].y + 1.4 * sqrt(kRibShape[k].z / 0.3)
                   + abs(rd.y / rd.z) * kRibFlow[k].w * 1.2 + (soft + (abs(t0) + 3.0) * (softT + 0.7 / uResolution.y)) * 24.0;
        if (abs(ro.y + rd.y * t0 - ribbonBaseY(k)) > band) return vec3(0.0);
    }
    float t = ribbonT(ro, rd, k);
    if (t <= 0.0 || t > tMax) return vec3(0.0);
    vec3 p = ro + rd * t;
    if (p.y < kFloorY) return vec3(0.0);
    float seen = t > gCut ? gBehind : 1.0;
    if (seen <= 0.0) return vec3(0.0);
    return ribbonLight(p.xy, soft + t * (softT + 0.7 / uResolution.y), k) * seen;
}

// All the ribbons along a ray (they glow, so their light adds). Written out
// one by one, so each ribbon's settings are fixed when the shader compiles.
// Reflections in the floor and the glass (`all` false) show only the hero
// sheaf, which keeps the cost down.
vec3 ribbonsAlong(vec3 ro, vec3 rd, float soft, float softT, float tMax, bool all) {
    vec3 acc = ribbonAlong(ro, rd, soft, softT, tMax, 0);
    if (all) acc += ribbonAlong(ro, rd, soft, softT, tMax, 1) + ribbonAlong(ro, rd, soft, softT, tMax, 2)
                  + ribbonAlong(ro, rd, soft, softT, tMax, 3);
    return acc;
}

// The room: deep navy, a little lighter up high, a faint pink haze behind the
// pink ribbon, a hot line where the floor meets the dark, and a little dust
// that sparkles with the treble.
vec3 horizonGlow(vec3 rd) {
    float band = exp(-max(rd.y, 0.0) * 90.0 - max(-rd.y, 0.0) * 22.0) * 0.7;   // hugs the floor, barely rises into the air
    vec3 c = mix(ribbonCyan * 0.3, ribbonPink * 0.75, smoothstep(-0.7, 0.2, rd.x));
    return c * band * exp(-abs(rd.x - 0.05) * 1.2) * (0.45 + 0.55 * uActive);
}
vec3 skyColour(vec3 rd) {
    vec3 col = mix(vec3(0.0, 0.058, 0.074), vec3(0.0, 0.076, 0.094), smoothstep(-0.05, 0.5, rd.y));
    col += vec3(0.03, 0.004, 0.03) * exp(-length(rd.xy - vec2(0.12, 0.10)) * 3.5) * (0.3 + 0.4 * uActive);
    col += vec3(0.0, 0.04, 0.05) * exp(-length(rd.xy - vec2(-0.45, -0.12)) * 3.0) * (0.3 + 0.4 * uActive);
    vec2 sp = rd.xy / max(rd.z, 0.2) * 90.0 + vec2(sceneShift() * 5.0, 0.0);   // far dust drifts slowest
    vec2 cell = floor(sp);
    float h = hash21(cell);
    vec2 off = vec2(hash21(cell + 7.1), hash21(cell + 3.3)) - 0.5;
    float star = smoothstep(0.12, 0.0, length(fract(sp) - 0.5 - off * 0.6)) * step(0.95, h);
    float tw = 0.4 + 0.6 * sin(h * 50.0 + uTime * 2.0);
    col += vec3(0.5, 0.85, 1.0) * star * tw * (0.15 + 0.4 * uTreble) * (0.3 + 0.7 * uActive);
    return col + horizonGlow(rd);
}

// Lights for the glass to reflect: the pink and cyan of the ribbons.
vec3 envColour(vec3 rd) {
    vec3 col = skyColour(rd);
    col += ribbonPink * 1.4 * pow(max(dot(rd, normalize(vec3(0.6, 0.25, -0.7))), 0.0), 14.0);
    col += ribbonCyan * 1.1 * pow(max(dot(rd, normalize(vec3(-0.7, 0.1, -0.6))), 0.0), 14.0);
    col += vec3(0.8, 0.95, 1.0) * 0.5 * pow(max(dot(rd, normalize(vec3(0.1, 1.0, -0.3))), 0.0), 16.0);
    return col;
}

// Where each glass shape is, how it's turned, and how big it is. Each one
// sits at its own depth and slides with the scene, most of them left and a
// few the other way; once it's off one edge it comes back in from the other
// (a little higher or lower, a bit bigger or smaller, turned differently),
// so the stream never runs dry. The starting points are staggered so the
// shapes don't travel as one clump.
void gemPose(int j, out vec3 c, out vec2 ang, out float r, out int faces) {
    float fj = float(j);
    if (j == 0)      { c = vec3(0.0, -0.40, -1.10); r = 0.17; faces = 10; }
    else if (j == 1) { c = vec3(0.0, 0.95, 2.60);   r = 0.34; faces = 10; }
    else if (j == 2) { c = vec3(0.0, 0.70, 0.40);   r = 0.30; faces = 10; }
    else if (j == 3) { c = vec3(0.0, -0.50, 1.20);  r = 0.22; faces = 4; }
    else if (j == 4) { c = vec3(0.0, 0.30, -0.60);  r = 0.13; faces = 10; }
    else if (j == 5) { c = vec3(0.0, 1.25, 1.60);   r = 0.24; faces = 10; }
    else if (j == 6) { c = vec3(0.0, -0.40, 3.20);  r = 0.34; faces = 10; }
    else             { c = vec3(0.0, 0.55, -1.50);  r = 0.10; faces = 10; }
    // Wrap span: wide enough that the jump happens off screen at this depth.
    float depth = c.z + 3.4;
    float span = 2.0 * (depth * 0.85 + r) + 0.4;
    // Three of them drift the other way, left to right, a little slower.
    float flow = (j == 1 || j == 3 || j == 6) ? -0.7 : 1.0;
    float x = fract(fj * 0.375 + 0.62) * span - sceneShift() * flow;
    float lap = floor(x / span);
    c.x = x - lap * span - 0.5 * span;
    float lh = hash11(lap * 3.7 + fj * 11.3);
    c.y = clamp(c.y + (lh - 0.5) * 0.4, kFloorY + r * 0.8, 2.0);
    r *= 0.85 + 0.3 * hash11(lap * 5.1 + fj * 2.9);
    c.y += 0.05 * sin(uTime * 0.45 + fj * 1.9);
    ang = vec2(uTime * (0.10 + 0.03 * fj) + uClock * 0.02 + fj * 1.3 + lh * 6.28,
               beatTurn() * 0.32 + fj * 2.1);
    r *= 1.32 * (1.0 + 0.04 * gSwell);
}

vec3 toGem(vec3 v, vec2 ang) { v.xz = rot(ang.x) * v.xz; v.yz = rot(ang.y) * v.yz; return v; }
vec3 fromGem(vec3 v, vec2 ang) { v.yz = rot(-ang.y) * v.yz; v.xz = rot(-ang.x) * v.xz; return v; }

// Ray against a convex polyhedron (object space). Returns entry/exit distance
// and the face normals there; tNear > tFar means a miss.
void gemHit(vec3 ro, vec3 rd, int faces, float h, out float tNear, out float tFar, out vec3 nNear, out vec3 nFar) {
    tNear = -1e5; tFar = 1e5; nNear = vec3(0.0, 1.0, 0.0); nFar = nNear;
    for (int i = 0; i < 10; i++) {
        if (i >= faces) break;
        vec3 n = kFaceAxes[i];
        float dn = dot(rd, n), on = dot(ro, n);
        if (abs(dn) < 1e-5) { if (abs(on) > h) tNear = 1e5; continue; }
        float t1 = (-h - on) / dn, t2 = (h - on) / dn;
        vec3 sn = n * -sign(dn);
        if (min(t1, t2) > tNear) { tNear = min(t1, t2); nNear = sn; }
        if (max(t1, t2) < tFar) { tFar = max(t1, t2); nFar = -sn; }
    }
}

// How close a surface point is to an edge (x: 1 on an edge, 0 mid-face) and
// to a corner (y: 1 where three faces meet).
vec2 gemEdge(vec3 p, int faces, float h, float w) {
    float a = -1e5, b = -1e5, c = -1e5;
    for (int i = 0; i < 10; i++) {
        if (i >= faces) break;
        float d = abs(dot(p, kFaceAxes[i])) - h;
        if (d > a) { c = b; b = a; a = d; } else if (d > b) { c = b; b = d; } else if (d > c) { c = d; }
    }
    return vec2(1.0 - smoothstep(0.0, w, a - b), 1.0 - smoothstep(0.0, w * 2.5, a - c));
}

// Nearest glass shape along a ray, if any.
bool nearestGem(vec3 ro, vec3 rd, float tMax, out int jHit, out float tHit) {
    jHit = -1; tHit = tMax;
    for (int j = 0; j < kGems; j++) {
        vec3 c; vec2 ang; float r; int faces;
        gemPose(j, c, ang, r, faces);
        vec3 oc = ro - c;
        float bb = dot(oc, rd), cc = dot(oc, oc) - r * r;
        if (bb * bb - cc < 0.0 || -bb + sqrt(max(bb * bb - cc, 0.0)) < 0.0) continue;
        float h = r * (faces == 4 ? 0.5774 : 0.7947);
        float tn, tf; vec3 nn, nf;
        gemHit(toGem(oc, ang), toGem(rd, ang), faces, h, tn, tf, nn, nf);
        if (tn < tf && tn > 0.0 && tn < tHit) { tHit = tn; jHit = j; }
    }
    return jHit >= 0;
}

// Cyan glow around each shape's silhouette, so the glass seems to give off
// light. pool is the glow the shapes throw on the floor, if ro is on it.
vec3 gemHalo(vec3 ro, vec3 rd, float tMax, out vec3 pool) {
    vec3 acc = vec3(0.0);
    pool = vec3(0.0);
    for (int j = 0; j < kGems; j++) {
        vec3 c; vec2 ang; float r; int faces;
        gemPose(j, c, ang, r, faces);
        vec2 gd = (ro.xz - c.xz) / r;
        pool += glassCyan * exp(-dot(gd, gd) * 0.8 - max(c.y - r - kFloorY, 0.0) * 2.5);
        float t = max(dot(c - ro, rd), 0.0);
        if (t > tMax) continue;
        float d = max(length(ro + rd * t - c) - r * 0.85, 0.0);
        acc += glassCyan * exp(-d * 9.0) * 0.06 / (1.0 + t * 0.15);
    }
    return acc * (0.4 + 0.6 * uActive + 0.5 * gSwell);
}

// One glass shape: reflection, refraction of the ribbons behind it, and its
// glowing cyan edges, front and back. Shapes far from the focus distance
// get softer, wider edges, like a lens out of focus.
vec3 shadeGem(int j, vec3 ro, vec3 rd, float tHit, bool full) {
    vec3 c; vec2 ang; float r; int faces;
    gemPose(j, c, ang, r, faces);
    float h = r * (faces == 4 ? 0.5774 : 0.7947);
    vec3 oro = toGem(ro - c, ang), ord = toGem(rd, ang);
    float tn, tf; vec3 nn, nf;
    gemHit(oro, ord, faces, h, tn, tf, nn, nf);
    vec3 pIn = oro + ord * tn;
    vec3 nW = fromGem(nn, ang);
    float px = 2.0 / uResolution.y;
    float blur = smoothstep(4.8, 7.5, length(c - ro));
    float edgeW = min((px * tn * 1.6 + 0.004 + blur * 0.012) / r * h, h * 0.04);
    vec2 edgeFront = gemEdge(pIn, faces, h, edgeW);

    float fres = 0.07 + 0.93 * pow(1.0 - clamp(-dot(rd, nW), 0.0, 1.0), 4.0);
    vec3 rr = reflect(rd, nW);
    vec3 col = envColour(rr) * fres * 0.8;
    float facet = hash11(dot(nn, vec3(12.9, 78.2, 37.7)));   // each face catches the light a little differently
    // Solid clear glass: every face catches the room's pink or cyan light a
    // little differently, a cyan body that deepens with thickness, a hot
    // glint where a face turns a light at you, and pink caught inside.
    float faceLit = 0.5 + 0.5 * dot(nW, normalize(vec3(0.5, 0.6, -0.6)));
    col += mix(ribbonPink, ribbonCyan, step(0.55, facet)) * (0.02 + 0.1 * facet * facet) * faceLit * (0.5 + 0.5 * uActive);
    float thick = clamp((tf - tn) / r, 0.0, 2.0);
    col += glassCyan * 0.045 * thick * (0.5 + 0.5 * uActive);
    col += ribbonPink * 0.2 * smoothstep(h, -h, pIn.y) * thick * (0.4 + 0.6 * uActive);
    float g1 = max(dot(rr, normalize(vec3(0.6, 0.25, -0.7))), 0.0);
    float g2 = max(dot(rr, normalize(vec3(0.1, 1.0, -0.3))), 0.0);
    g1 *= g1; g1 *= g1; g1 *= g1; g1 *= g1; g1 *= g1;      // ^32
    g2 *= g2; g2 *= g2; g2 *= g2; g2 *= g2; g2 *= g2;
    col += (vec3(1.0, 0.75, 0.9) * g1 + vec3(0.85, 1.0, 1.0) * g2) * 1.6;
    float lit = (0.45 + 0.55 * uActive) * (0.9 + 0.2 * uTreble + 0.5 * gSwell);

    if (full) {
        // Through the glass: bend in, cross to the far face, bend out.
        vec3 d1 = refract(ord, nn, 1.0 / 1.15);
        float tn2, tf2; vec3 nn2, nf2;
        gemHit(pIn + d1 * 0.002, d1, faces, h, tn2, tf2, nn2, nf2);
        vec3 pOut = pIn + d1 * (0.002 + tf2);
        vec3 wOut = fromGem(pOut, ang) + c;
        // Leaving the glass. Its faces come in parallel pairs, so the light
        // leaves in the direction it came in, shifted sideways by its path
        // through the glass. That path is smooth across each face, so the
        // view through a face is too. (Bending it out by whichever far face
        // it happened to reach split each face into hard-edged patches, dark
        // where that face reflected the light back in.)
        vec3 behind = skyColour(rd) + ribbonsAlong(wOut, rd, 0.002, 0.0, 30.0, false) * 0.7;
        // Towards the rim more of the light reflects back inside, so the glass
        // shades down to the dark room there: one smooth roll-off over the
        // rounded shape, rather than face by face.
        float rim = 1.0 - abs(dot(ord, normalize(pIn)));
        behind = mix(behind, skyColour(fromGem(reflect(d1, -nf2), ang)) * 1.3, smoothstep(0.2, 0.75, rim) * 0.9);
        col += behind * vec3(0.8, 1.0, 1.05) * (1.0 - fres) * (1.0 + 0.5 * facet);
        vec2 edgeBack = gemEdge(pOut, faces, h, edgeW * 1.2);
        // A ribbon passing through the glass lights it from inside.
        float tz = ribbonT(ro, rd, 0);
        if (tz > tHit && tz < tHit + (tf - tn)) col += ribbonLight((ro + rd * tz).xy, 0.0, 0) * 0.6;
        col += glassCyan * (edgeBack.x * 0.12 + edgeBack.y * 0.15) * lit * (1.0 - 0.7 * blur);
    }
    vec2 edgeGlow = gemEdge(pIn, faces, h, min(edgeW * 3.0, h * 0.08));
    vec3 edgeHot = mix(glassCyan, vec3(0.75, 1.0, 1.0), 0.1);   // edges burn a little whiter than their glow
    col += edgeHot * (edgeFront.x * 1.5 + edgeFront.y * 1.4) * (1.0 - 0.6 * blur) * lit;
    col += glassCyan * edgeGlow.x * 0.75 * (1.0 - 0.6 * blur) * lit;
    return col;
}

void main() {
    gSwell = beatSwell();
    gSpec = (ribbonSpec(0.05) + ribbonSpec(0.15) + ribbonSpec(0.25)) / 3.0;
    gPlay = playAmount();
    gGlass = smoothstep(0.35, 1.0, gPlay);
    gPx = 3.4 / 1.3 / uResolution.y;
    gDrive = pulseDrive();
    float lines = 1.0 - smoothstep(0.0, 0.3, gPlay);  // the exact lines take over from the ribbons at the end
    vec2 p = screenP();
    // Camera sits low by the glossy floor and sways slowly (on the
    // sound-gated clock); it leans in a little on the bass. In silence it
    // turns to face straight ahead, so the lines lie level.
    // The scene itself slides left past it (sceneShift).
    // The camera turns toward the pointer, so moving the mouse looks
    // around the room the ribbons are flying through. It is a small
    // turn: the ribbons are the subject, not the camera.
    float yaw = 0.12 * sin(uTime * 0.07) * gPlay + pointerAt().x * 0.08 * uMouseHere;
    // The lens is shifted up rather than tilted, so the horizon sits low and
    // the glass stays upright.
    vec3 ro = vec3(0.0, -0.40, -3.3 + 0.06 * gSwell);
    ro.xz = rot(yaw) * ro.xz;
    vec3 fw = vec3(-sin(yaw), 0.0, cos(yaw));
    vec3 rt = vec3(cos(yaw), 0.0, sin(yaw));
    vec3 rd = normalize(p.x * rt + vec3(0.0, p.y + 0.24 + pointerAt().y * 0.04 * uMouseHere, 0.0) + 1.3 * fw);

    float tFloor = rd.y < 0.0 ? (kFloorY - ro.y) / rd.y : 1e5;
    vec3 sky = skyColour(rd);
    vec3 col = sky;
    bool ribbons = lines < 1.0;                      // fully settled: only the exact lines are left

    int j = -1; float tGem = tFloor;
    bool hitGem = gGlass > 0.0 && nearestGem(ro, rd, tFloor, j, tGem);
    if (tFloor < 1e4 && (!hitGem || gGlass < 1.0)) {
        // Glossy black floor: a near-mirror of the ribbons and the glass,
        // softening with distance and fading into the hot horizon line.
        vec3 fp = ro + rd * tFloor;
        vec3 rr = reflect(rd, vec3(0.0, 1.0, 0.0));
        float fres = 0.30 + 0.70 * pow(1.0 - clamp(-rd.y, 0.0, 1.0), 5.0);
        vec3 refl = skyColour(rr) * 0.8;
        vec3 glassPool = vec3(0.0);
        if (gGlass > 0.0) {
            int j2; float t2;
            if (nearestGem(fp, rr, 40.0, j2, t2)) refl = mix(refl, shadeGem(j2, fp, rr, t2, false), gGlass);
            refl += gemHalo(fp, rr, 40.0, glassPool) * 2.0 * gGlass;
        }
        if (ribbons) refl += ribbonsAlong(fp, rr, 0.01, 0.035, 40.0, false) * (1.0 - lines);
        col = vec3(0.002, 0.006, 0.014) + refl * fres * 0.75;
        // the ribbons' light pooling on the floor beneath them
        float pool = (exp(-abs(fp.z - ribbonDepth(fp.x, 0)) * 0.8) + 0.35 * exp(-abs(fp.z - ribbonDepth(fp.x, 2)) * 1.6))
                   * smoothstep(-4.8, -2.5, fp.x) * smoothstep(4.8, 2.5, fp.x);
        col += ribbonPink * 0.6 * pool * (0.4 + 0.6 * uActive) * (0.85 + 0.25 * gSwell);
        col += glassPool * 0.8 * (0.4 + 0.6 * uActive) * gGlass;
        col = mix(col, sky, smoothstep(8.0, 30.0, tFloor));
    }
    // Ribbons in front of whatever we hit; as the glass fades, the ribbons
    // behind it show through.
    if (hitGem) col = mix(col, shadeGem(j, ro, rd, tGem, true), gGlass);
    if (ribbons) {
        gCut = hitGem ? tGem : 1e9;
        gBehind = 1.0 - gGlass;
        col += ribbonsAlong(ro, rd, 0.0, 0.0, tFloor, true) * (1.0 - lines);
    }
    if (gGlass > 0.0) {
        vec3 unused;
        col += gemHalo(ro, rd, hitGem ? tGem : tFloor, unused) * gGlass;
    }
    col += ribbonPink * gSwell * 0.015;

    col = 1.0 - exp(-col * 1.8);                   // soft highlight roll-off
    col *= 1.0 - 0.3 * dot(p, p);                   // vignette
    col = mix(col * 0.5, col, uActive);             // dims in silence
    // The ten still lines, in the ribbons' resting pink.
    col = mix(col, vec3(0.92, 0.2, 0.52), idleLines() * lines);
    fragColor = vec4(col, 1.0);
}