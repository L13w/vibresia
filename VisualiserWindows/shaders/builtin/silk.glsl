// Silk Flight: flying forward through the music. A round satin ribbon, a
// glossy hot-pink tube with twisting folds, sweeps in from both frame edges
// and winds away into a teal nebula that billows and breathes with the
// music, turning electric violet in the distance, while chunky cyan glass
// blocks tumble past on near-black space, never touching it or each other.
const int kSilkSteps = 31;       // depth samples per ribbon branch (out to z ~17, where it has faded out)
const int kSilkGlass = 8;        // glass blocks
const float kSilkRibs = 12.0;    // ribs round each tube (about half face us)

const vec3 silkPink   = vec3(1.00, 0.36, 0.62);
const vec3 silkPinkHi = vec3(1.00, 0.66, 0.84);
const vec3 silkFold   = vec3(0.72, 0.14, 0.46);
const vec3 silkLilac  = vec3(0.90, 0.49, 0.99);
const vec3 silkViolet = vec3(0.31, 0.22, 0.86);
const vec3 silkCyan   = vec3(0.00, 1.00, 0.89);
const vec3 silkDust   = vec3(0.20, 0.43, 0.47);

// Smooth clocks. uTime only runs while there's sound and eases in and out of
// it, so flying on it makes the camera accelerate and decelerate smoothly; a
// small share of uScroll (distance that runs faster when louder, and never
// backwards) lets it speed up a little with the music. Nothing here steps
// on the beat.
float silkTravel() { return uTime * 1.2 + uScroll * 0.35; }
float silkFlow() { return uTime; }

// A soft swell on every kick, in place of the raw bass and beat-strength
// inputs (which jump from one frame to the next): it eases in over about
// 150 ms on the (continuous) beat clock and dies away over about a second,
// sized by the slow bass. Summing the last few beats keeps it continuous.
float silkSwellAt(float x) { return smoothstep(0.0, 0.3, x) * exp(-x * 1.5); }
float silkSwell() {
    float ph = fract(uBeats);
    return (silkSwellAt(ph) + silkSwellAt(ph + 1.0) + silkSwellAt(ph + 2.0)) * clamp(0.3 + 2.8 * uBassSmooth, 0.0, 1.2) * uActive;
}
float gSilkSwell;   // silkSwell(), worked out once per pixel at the top of main()
float gSilkFlow;
float gSilkMid;     // snares and claps: the upper mids, shaped 0..1
float gSilkHi;      // hats and cymbals: the top of the spectrum, shaped 0..1
vec4 gSilkSway;    // slow phase drifts for the ribbon's curves, from gSilkFlow
float gSilkW;      // the tube's half-width across its cross-section, swelling a little on each kick
float gSilkH;      // the tube's half-thickness (its cross-section is an ellipse W x H)
float gSilkLv;     // smoothed loudness and bass, shaped 0..1, for the sky
float gSilkBs;

// Ribbon centre (xy) and twist (z) at world depth s, for branch b (0 or 1).
// Near the camera each branch is pushed out to its own side, so it sweeps
// in from the frame edge and never closes over the lens.
// Depth terms of the ribbon path, shared by both branches at depth z.
vec4 silkDepth(float z) {
    return vec4(1.8 * exp(-0.5 * z), 1.5 * smoothstep(1.5, 7.0, z), 0.4 * exp(-0.12 * z), 0.75 * exp(-0.06 * z));
}
vec3 silkBranch(float s, float b, vec4 k) {
    float side = b < 0.5 ? -1.0 : 1.0;
    float cross = side * (0.75 + k.x - k.y);
    vec4 f = gSilkSway;   // slow air: each curve's phase drifts, so the ribbon sways
    return vec3(cross + 1.1 * sin(s * 0.7 + b * 2.4 + f.x) + k.z * sin(s * 1.13 + b * 5.1 + f.y),
                side * (-0.25 - 0.55 * k.x) + 0.05 + 0.45 * sin(s * 0.47 + b * 1.7 + 2.0 + f.z),
                0.5 + k.w * sin(s * 0.41 + b * 3.0 + 1.0 + f.w));
}
// The two branches are one band: deeper in, each eases onto their shared
// middle line, so the ribbon comes in from one side, loops away through the
// distance and comes back out on the other, all in one piece.
float silkJoin(float z) { return smoothstep(2.6, 6.5, z); }
vec3 silkJoined(vec3 c, vec3 o, float j) { return mix(c, 0.5 * (c + o), j); }
vec3 silkPath(float s, float z, float b) {
    vec4 k = silkDepth(z);
    return silkJoined(silkBranch(s, b, k), silkBranch(s, 1.0 - b, k), silkJoin(z));
}

// The ray point at depth z in the tube's cross-section, scaled so the tube's
// elliptical outline is the unit circle (x across its width, y through it).
// The tube tapers into the distance, so a stretch heading away from us thins
// to a silky tail instead of showing a blunt round end.
float silkTaper(float z) { return mix(1.0, 0.32, smoothstep(1.5, 9.0, z)); }
vec2 silkTube(vec2 rd, float z, vec3 c, vec2 iwh) {
    vec2 q = rd * z - c.xy;
    vec2 t = vec2(cos(c.z), sin(c.z));
    return vec2(dot(q, t), dot(q, vec2(-t.y, t.x))) * iwh / silkTaper(z);
}

// Where the ray, moving from a0 to a1 (cross-section coordinates) between
// two depth samples, first enters the tube: depth, the entry point on the
// outline, and coverage (soft over about a pixel at the silhouette, so the
// edge stays smooth). Solved exactly for the straight segment, so a thin
// part of the tube can't slip between samples.
vec4 silkCross(vec2 a0, vec2 a1, float z0, float z1, float aa) {
    vec2 da = a1 - a0;
    float A = max(dot(da, da), 1e-8);
    float bq = dot(a0, da);
    float k = clamp(-bq / A, 0.0, 1.0);
    float dmin = length(a0 + da * k);
    // A near miss counts (as partial coverage) only where it is closest
    // inside this step; at either end the neighbouring step decides.
    if (dmin > 1.0 + aa || (dmin >= 1.0 && (k <= 0.0 || k >= 1.0))) return vec4(1e5, 0.0, 0.0, 0.0);
    float disc = bq * bq - A * (dot(a0, a0) - 1.0);
    float ke = disc > 0.0 ? clamp((-bq - sqrt(disc)) / A, 0.0, k) : k;
    vec2 a = a0 + da * ke;
    a /= max(length(a), 1e-4);
    // Coverage eases off over a pixel only where the ray grazes the outline
    // within this step; a ray still heading inward at the step's end is in.
    float cov = k >= 1.0 || k <= 0.0 ? 1.0 : 1.0 - smoothstep(1.0 - aa, 1.0 + aa, dmin);
    return vec4(mix(z0, z1, ke), a, cov);
}

float facingOf(vec3 n, vec2 rd) { return clamp(-dot(n, normalize(vec3(rd, 1.0))), 0.0, 1.0); }
vec3 silkShade(vec2 rd, float z, float s, vec2 a, float b) {
    vec3 c = silkPath(s, z, b);
    vec3 c2 = silkPath(s + 0.15, z + 0.15, b);
    vec2 t = vec2(cos(c.z), sin(c.z));
    vec3 D = normalize(vec3((c2.xy - c.xy) / 0.15, 1.0));   // along the tube
    // The outline's normal at the entry point, turned square to the tube.
    vec2 g = vec2(a.x / gSilkW, a.y / gSilkH);       // the taper scales both alike
    vec3 N = vec3(t * g.x + vec2(-t.y, t.x) * g.y, 0.0);
    N = normalize(N - D * dot(N, D));
    vec3 V = normalize(vec3(rd, 1.0));
    vec3 R = normalize(cross(D, N));                 // round the tube
    float f = gSilkFlow;
    // Satin folds wound round the tube, twisting along its length and slowly
    // re-folding; snares and claps send a ripple running down it.
    float th = atan(a.y, a.x);
    float tw = th * 3.0 + s * 1.5 + c.z * 3.0 + 0.6 * sin(s * 0.3 + b * 3.0 + f * 0.21);
    float fold = sin(tw);
    float rip = gSilkMid * sin(s * 2.6 - f * 7.0 + b * 2.0);
    // Ribs: fine corded ridges running the length of the tube, like the
    // film's ribbed satin, turning with its twist; each catches its own
    // highlight. They soften away where they would get finer than a few
    // pixels (far off, and toward the outline) so they never shimmer.
    float rp = th * kSilkRibs + 0.3 * s + 0.5 * fold;
    float ribPx = 6.2832 * gSilkH * silkTaper(z) / kSilkRibs * uResolution.y / (1.25 * z) * (0.25 + 0.75 * facingOf(N, rd));
    float ribA = smoothstep(2.0, 5.0, ribPx) * 0.85;
    float ridge = sin(rp);
    vec3 Np = normalize(N + R * (0.16 * fold + 0.6 * ribA * cos(rp)) + D * (0.1 * sin(s * 0.8 + f * 0.4) + 0.05 * rip));
    float facing = clamp(-dot(N, V), 0.0, 1.0);
    vec3 L = normalize(vec3(0.35, 0.75, -0.55));
    float dif = max(dot(Np, L), 0.0);
    vec3 H = normalize(L - V);
    float spec = pow(max(dot(Np, H), 0.0), 40.0) * 1.6 + 0.3 * pow(max(dot(Np, H), 0.0), 6.0);
    // Satin's long highlight: the fibres run along the tube, so the sheen is
    // a streak down its length on the lit side.
    float dh = dot(D, H);
    float aniso = pow(sqrt(max(1.0 - dh * dh, 0.0)), 36.0) * smoothstep(-0.2, 0.5, dot(N, L))
                * (0.75 + 0.25 * sin(th * 23.0 + 2.0 * fold));   // fine satin grain in the streak
    // Satin: a ramp from deep magenta through hot pink to a pale pink sheen.
    float k = clamp(0.2 + 0.75 * dif + 0.08 * fold, 0.0, 1.0);
    vec3 hot = vec3(1.0, 0.24, 0.7);                 // the film's hot magenta-pink
    vec3 col = k < 0.5 ? mix(silkFold * 0.8, hot, k * 2.0) : mix(hot, silkPinkHi, k * 2.0 - 1.0);
    // Round: falls away toward the edges, and the underside sits in its own
    // soft shadow, cooling to violet.
    float under = smoothstep(0.15, -0.75, Np.y);
    col *= (0.6 + 0.4 * sqrt(facing)) * (1.0 - 0.35 * under);
    col = mix(col, silkViolet * 0.8, under * 0.45);
    col *= 0.75 + 0.25 * smoothstep(-1.0, 0.4, fold);  // the creases between folds
    col *= 1.0 - ribA * 0.35 * smoothstep(0.2, -1.0, ridge);   // the grooves between ribs
    col += vec3(1.0, 0.8, 0.94) * (spec + 1.1 * aniso) * (1.2 + 0.3 * gSilkSwell + 0.4 * gSilkHi);
    // Rim light round the outline: cyan from above, lilac from below.
    float rim = pow(1.0 - facing, 3.0);
    col += mix(silkLilac * 0.8, vec3(0.45, 0.95, 1.0), smoothstep(-0.3, 0.6, N.y)) * rim * (0.55 + 0.15 * gSilkSwell);
    // Hats and cymbals: glitter catching round the sides of the tube.
    vec2 gq = vec2(th * 8.0, s * 40.0);
    vec2 gc = floor(gq);
    float gh = hash21(gc + b * 13.0);
    float glit = step(0.8, gh) * (gh * 4.0 - 3.0) * (1.0 - smoothstep(0.12, 0.38, length(fract(gq) - 0.5)))
               * smoothstep(0.35, 0.8, 1.0 - facing) * (1.0 - smoothstep(3.0, 6.0, z));
    col += vec3(1.0, 0.9, 1.0) * glit * gSilkHi * 1.2;
    // Far away the ribbon cools to electric violet-blue and sinks into the dust.
    float far = smoothstep(1.8, 7.0, z);
    float band = 0.9 + 0.15 * bandAt(clamp(z / 18.0, 0.0, 1.0)) + 0.1 * gSilkSwell;     // a faint spectrum glow on the far ribbon
    col = mix(col, (mix(silkViolet, silkLilac, 0.35 * dif + spec) * (0.6 + 0.6 * dif) * (0.6 + 0.4 * sqrt(facing)) + spec * 0.4 + rim * silkLilac * 0.3) * band, far * 0.9);
    col = mix(col, silkDust * 0.6, smoothstep(12.0, 30.0, z));
    return col;
}

vec4 silkScene(vec2 p, vec2 rd, float travel);   // below

// Glass block sizes (half-extents: a mix of big chunky slabs and small
// cubes) and their depth slots.
const vec3 kSilkSize[8] = vec3[8](vec3(0.739, 0.605, 0.694), vec3(0.358, 0.314, 0.347), vec3(0.560, 0.470, 0.538), vec3(0.806, 0.582, 0.672), vec3(0.336, 0.302, 0.370), vec3(0.582, 0.448, 0.526), vec3(0.694, 0.650, 0.627), vec3(0.403, 0.347, 0.381));
const float kSilkSlot[8] = float[8](0.000, 1.820, 3.367, 5.524, 7.358, 8.896, 10.988, 12.831);

// Glass block i: where it is, its size, how it is turned, and how faded in.
// Each block keeps clear of both satin tubes: it is pushed out from the
// nearest point of each tube's cross-section at its depth with a soft
// minimum distance, so it glides round the satin instead of through it.
// The blocks never touch each other either: they all fly at the same speed,
// spaced in depth so that any two are always further apart than their two
// reaches put together, so however they turn and drift, no two can overlap.
void silkBlock(int i, float travel, out vec3 c, out vec3 hs, out vec2 ang, out float fade) {
    float n = float(i);
    // Depth slots: each gap is the two neighbours' reaches added (plus 0.05),
    // so any two blocks are always further apart in depth than they can reach.
    float span = 14.716;
    float z = 0.9 + mod(kSilkSlot[i] - travel, span);
    fade = smoothstep(13.0, 7.0, z) * smoothstep(1.5, 2.5, z);
    hs = kSilkSize[i];
    ang = vec2(uTime * (0.32 + 0.2 * hash11(n * 9.1)) + n, uTime * (0.12 + 0.15 * hash11(n * 1.3)) + n * 2.0);
    float clear = length(hs) + 0.4 + gSilkH;         // its reach, the tube's thickness, and room for its slope over the block's depth
    // Each block rides beside one satin band, on its own side of it and
    // never closer than its reach, so it drifts along with the satin and
    // can never pass through that band.
    float own = mod(n, 2.0);
    float side = hash11(n * 4.1 + 0.2) < 0.5 ? -1.0 : 1.0;
    vec3 rp = silkPath(travel + z, z, own);
    vec2 t = vec2(cos(rp.z), sin(rp.z));
    float gap = clear + 0.35 + (0.1 + 0.7 * hash11(n * 2.3 + 0.53)) * (0.3 + 0.12 * z);
    c = vec3(rp.xy + t * (hash11(n * 7.13 + 0.17) - 0.5) * 1.4 * gSilkW + vec2(-t.y, t.x) * side * gap, z);
    // The other band: pushed out of its way with a soft minimum distance.
    // Where that band sweeps right across the block's lane it would have to
    // jump to the far side, so it fades out as the band closes in instead.
    vec3 ro = silkPath(travel + z, z, 1.0 - own);
    vec2 to = vec2(cos(ro.z), sin(ro.z));
    vec2 P = ro.xy + to * clamp(dot(c.xy - ro.xy, to), -gSilkW, gSilkW);
    vec2 dv = c.xy - P;
    float d = max(length(dv), 1e-4);
    float co = clear + 0.25;                          // a little more room from the band it doesn't ride
    float nd = co + 0.15 * log(1.0 + exp((d - co) / 0.15));   // soft max(d, co)
    c.xy = P + dv / d * nd;
    fade *= smoothstep(0.3, 0.65, d / co);
}

// Ray/box slabs against block i, in the block's own frame.
vec2 silkSlab(vec3 rd, vec3 c, vec3 hs, vec2 ang, out vec3 ro, out vec3 d) {
    mat2 r1 = rot(ang.x), r2 = rot(ang.y);
    ro = -c;
    ro.xz = r1 * ro.xz; ro.xy = r2 * ro.xy;
    d = rd;
    d.xz = r1 * d.xz; d.xy = r2 * d.xy;
    vec3 m = 1.0 / d;
    vec3 k = abs(m) * hs;
    vec3 t1 = -m * ro - k, t2 = -m * ro + k;
    return vec2(max(max(t1.x, t1.y), t1.z), min(min(t2.x, t2.y), t2.z));
}

// A box-space direction back in the world.
vec3 silkUnturn(vec3 v, vec2 ang) {
    v.xy = rot(-ang.y) * v.xy;
    v.xz = rot(-ang.x) * v.xz;
    return v;
}

// Shade the glass: a clear, slightly cyan body that shows the satin and the
// space behind it, bent by the faces; bright bevelled edges with a touch of
// colour fringing; fresnel rims; inner reflections; and a sharp glint.
vec3 silkGlassShade(vec3 col, vec2 p, vec3 rd, float travel, int i, vec3 c, vec3 hs, vec2 ang, float fade) {
    float n = float(i);
    vec3 ro, d;
    vec2 tt = silkSlab(rd, c, hs, ang, ro, d);
    float tN = tt.x, tF = tt.y;
    vec3 pf = (ro + d * tN) / hs, pb = (ro + d * tF) / hs;
    vec3 af = abs(pf), ab = abs(pb);
    float px = 1.5 * tN / uResolution.y * 1.6 / min(min(hs.x, hs.y), hs.z);
    vec3 ef = 1.0 - af, eb = 1.0 - ab;
    float edgeF = min(min(max(ef.x, ef.y), max(ef.y, ef.z)), max(ef.x, ef.z));
    float edgeB = min(min(max(eb.x, eb.y), max(eb.y, eb.z)), max(eb.x, eb.z));
    vec3 nf = step(vec3(max(max(af.x, af.y), af.z)), af) * sign(pf);
    vec3 nb = step(vec3(max(max(ab.x, ab.y), ab.z)), ab) * sign(pb);
    vec3 nfW = silkUnturn(nf, ang), nbW = silkUnturn(nb, ang);
    vec3 dn = normalize(d);
    float thick = clamp((tF - tN) / (2.0 * hs.z), 0.0, 1.5);
    float th = min(thick, 1.0);

    // What lies behind, seen through the block: bent by the front face and
    // again by the back face, as a slab of glass would.
    vec2 off = -(nfW.xy * 0.11 + nbW.xy * 0.07) * (0.6 + 0.4 * th);
    vec4 behind = silkScene(p + off / 1.25, rd.xy + off, travel);
    vec3 bc = behind.w < tN ? col : behind.rgb;      // satin nearer than the block stays as it was
    // Smoky glass: what's behind darkens fast with thickness, under a faint
    // teal haze, so the body reads deep and the edges carry the light.
    vec3 g = bc * mix(vec3(0.36, 0.62, 0.68), vec3(0.05, 0.15, 0.2), th) + vec3(0.0, 0.03, 0.04) * (0.5 + th);

    vec3 L = normalize(vec3(0.4, 0.8, -0.4));
    L.xz = rot(ang.x) * L.xz; L.xy = rot(ang.y) * L.xy;
    float lit = max(dot(nf, L), 0.0);
    float fres = pow(1.0 - abs(dot(nf, dn)), 3.0);
    // Inner reflection: one face of each block catches a soft cyan sheen.
    float tone = hash11(dot(nf, vec3(1.0, 2.0, 3.0)) + n * 1.3 + 0.5);
    float sheet = step(0.38, tone) * smoothstep(-1.2, 1.0, dot(pf, vec3(0.6, -0.5, 0.4)) + 0.8 * (tone - 0.7));
    // Inner diagonals (reflections of the edges inside), front and back.
    float diag = abs(abs(ab.x * nf.y + ab.y * nf.z + ab.z * nf.x) - abs(ab.y * nf.y + ab.z * nf.z + ab.x * nf.x));
    float diagF = abs(abs(af.x * nf.y + af.y * nf.z + af.z * nf.x) - abs(af.y * nf.y + af.z * nf.z + af.x * nf.x));
    float lineD = (1.0 - smoothstep(0.012, 0.012 + px, diag)) * 0.55 + (1.0 - smoothstep(0.01, 0.01 + px, diagF)) * 0.4;
    // Hard reflection bands across the front faces.
    float st = abs(fract(dot(pf, vec3(0.55, 0.4, 0.5)) * 0.9 + hash11(n * 4.7)) - 0.5);
    float refl = 1.0 - smoothstep(0.05, 0.05 + px, st);
    float glint = pow(max(dot(reflect(dn, nf), L), 0.0), 80.0);

    g += silkCyan * (0.08 * lit + 0.5 * fres + 0.8 * sheet * sheet + 0.15 * refl) * (0.75 + 0.1 * gSilkSwell);
    g += vec3(0.7, 1.0, 1.0) * (0.25 * fres * fres + 0.12 * refl);
    // Bevelled edges: a bright core, a wider bevel fringed pink on one side
    // and cyan on the other, and a dark line where the bevel turns away.
    float e1 = 1.0 - smoothstep(0.03, 0.03 + px, edgeF);
    float e2 = 1.0 - smoothstep(0.12, 0.12 + 1.5 * px, edgeF);
    float e3 = 1.0 - smoothstep(0.145, 0.145 + 1.5 * px, edgeF);
    float b1 = 1.0 - smoothstep(0.018, 0.018 + px, edgeB);
    float fr = smoothstep(-0.4, 0.4, dot(nfW.xy, vec2(0.6, 0.8)));
    float lift = 0.9 + 0.25 * gSilkSwell;
    g *= 1.0 - 0.6 * (e3 - e2);
    float bev = (e2 - e1) * (0.3 + 0.7 * smoothstep(0.12, 0.03, edgeF));   // the bevel brightens toward the edge
    g += mix(vec3(1.0, 0.4, 0.85), vec3(0.3, 1.0, 1.0), fr) * bev * 1.15 * lift;
    g += vec3(0.45, 1.0, 0.98) * (e1 * 1.7 + b1 * 0.65 + lineD * 0.65) * lift;
    g += vec3(1.0) * glint * (1.8 + 2.0 * gSilkHi);
    return mix(col, g, fade);
}

float silkNoise(vec2 p) {
    vec2 i = floor(p), f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    return mix(mix(hash21(i), hash21(i + vec2(1.0, 0.0)), f.x),
               mix(hash21(i + vec2(0.0, 1.0)), hash21(i + vec2(1.0, 1.0)), f.x), f.y);
}

// Space and satin seen along rd (p is the matching screen point for the
// space layers). Returns the colour and the depth of the nearest satin.
vec4 silkScene(vec2 p, vec2 rd, float travel) {
    // Deep sky: a dim teal nebula that billows and drifts on the eased clock
    // (so it is still in silence), breathes brighter and swells outward with
    // the smoothed loudness and bass and the soft beat swell, and leans a
    // little violet with the mids and brighter with the highs. All eased;
    // nothing here steps on a beat.
    float f = gSilkFlow;
    float lv = gSilkLv, bs = gSilkBs;
    float swell = 1.0 + 0.18 * lv + 0.14 * bs + 0.05 * gSilkSwell;   // the cloud grows with the music
    vec2 wq = p * 1.4 + vec2(travel * 0.012, 0.0);
    vec2 warp = vec2(silkNoise(wq * 1.3 + vec2(f * 0.07, 1.7)), silkNoise(wq * 1.3 + vec2(5.2, 0.6 - f * 0.06))) - 0.5;
    warp *= 0.9 + 0.7 * bs;                          // billows harder with the bass
    vec2 nq = wq + warp;
    float neb = silkNoise(nq * 1.6 + vec2(0.0, f * 0.03)) * 0.62 + silkNoise(nq * 3.3 + vec2(3.0 - f * 0.05, 1.0)) * 0.38;
    vec3 col = vec3(0.0, 0.05, 0.025) + vec3(0.0, 0.05, 0.07) * smoothstep(0.7 * swell, 0.0, length(p * vec2(0.7, 1.4)));
    vec2 pg = p * vec2(1.1, 1.8) / swell;
    float glow = smoothstep(0.3, 0.95, neb) * exp(-dot(pg, pg));
    vec3 nebC = mix(vec3(0.04, 0.19, 0.23), vec3(0.17, 0.12, 0.34), 0.07 * gSilkMid + 0.3 * smoothstep(0.45, 0.9, neb));
    col += nebC * glow * (0.05 + 0.32 * lv + 0.25 * gSilkSwell * (0.4 + bs) + 0.03 * gSilkHi) * (0.35 + 0.65 * uActive);
    // Its bright heart: a drifting cloud of dust in the middle, carried on
    // the same billows.
    vec2 pc = (p - vec2(0.05, 0.03) - warp * 0.1) * vec2(1.8, 3.2) / swell;
    float core = exp(-dot(pc, pc));
    float dust = 0.0;
    if (core > 0.02) {                               // the cloud is nothing out at the corners
        vec2 dq = p * 2.2 + vec2(travel * 0.02, 0.0) + warp * 0.5;
        float dn = silkNoise(dq * 1.6) * 0.45 + silkNoise(dq * 3.4 + 7.0) * 0.27 + silkNoise(dq * 7.5 - 3.0) * 0.17 + silkNoise(dq * 16.0 + 1.0) * 0.11;
        dust = smoothstep(0.25, 0.85, dn) * core * 1.5;
    }
    col += silkDust * dust * 1.25 * (0.5 + 0.55 * lv + 0.25 * gSilkSwell + 0.04 * gSilkHi) * (0.6 + 0.4 * uActive);
    // Fine specks streaming outward as we fly.
    for (int l = 0; l < 3; l++) {
        float fl = float(l);
        float ph = fract(travel * 0.08 + fl / 3.0);
        vec2 sp = p * mix(14.0, 2.5, ph) * 30.0 + fl * 37.0;
        vec2 cell = floor(sp);
        float h = hash21(cell);
        float sd = length(fract(sp) - 0.5 - 0.3 * (vec2(hash21(cell + 3.1), hash21(cell + 5.7)) - 0.5));
        float pxc = mix(14.0, 2.5, ph) * 30.0 / uResolution.y;   // one pixel, in cells: soft enough not to shimmer
        float star = step(0.93, h) * (1.0 - smoothstep(0.05, 0.25 + 1.2 * pxc, sd)) / (1.0 + 6.0 * pxc * pxc) * smoothstep(0.0, 0.3, ph) * smoothstep(1.0, 0.8, ph);
        col += mix(vec3(0.75, 0.35, 0.9), vec3(0.8, 0.9, 1.0), hash21(cell + 9.0)) * star * (0.45 + 0.12 * gSilkSwell + 0.4 * gSilkHi * step(0.6, hash21(cell + 11.0))) * (0.4 + 0.6 * dust + 0.3 * uActive);
    }

    // The ribbon: two branches, each a round tube. Along the ray (exponential
    // depth steps, front to back) the first step where the ray enters a
    // tube's cross-section is solved exactly.
    vec2 iwh = vec2(1.0 / gSilkW, 1.0 / gSilkH);
    float aak = 1.5 * 1.25 / (uResolution.y * gSilkH);  // a pixel, in cross-section units per unit of depth
    vec4 hitA = vec4(1e5, 0.0, 0.0, 0.0), hitB = vec4(1e5, 0.0, 0.0, 0.0);   // z, entry point, coverage
    vec2 prevA = vec2(0.0), prevB = vec2(0.0);
    float zPrev = 0.0;
    for (int i = 0; i < kSilkSteps; i++) {
        float z = 0.45 * pow(1.13, float(i));
        float s = travel + z;
        vec4 dk = silkDepth(z);
        vec3 ca = silkBranch(s, 0.0, dk), cb = silkBranch(s, 1.0, dk);
        float jn = silkJoin(z);
        vec2 fa = silkTube(rd, z, silkJoined(ca, cb, jn), iwh);
        vec2 fb = silkTube(rd, z, silkJoined(cb, ca, jn), iwh);
        if (i > 0) {
            float aa = aak * z / silkTaper(z);
            if (hitA.x > 1e4) hitA = silkCross(prevA, fa, zPrev, z, aa);
            if (hitB.x > 1e4) hitB = silkCross(prevB, fb, zPrev, z, aa);
            if (hitA.x < 1e4 && hitB.x < 1e4) break;
        }
        prevA = fa; prevB = fb; zPrev = z;
    }
    float zr = 1e5;
    // Composite far branch first, then near.
    for (int j = 0; j < 2; j++) {
        vec4 h = (j == 0) == (hitA.x > hitB.x) ? hitA : hitB;
        float b = (j == 0) == (hitA.x > hitB.x) ? 0.0 : 1.0;
        if (h.x > 1e4) continue;
        float alpha = h.w * smoothstep(17.0, 9.0, h.x);
        if (h.w > 0.5) zr = min(zr, h.x);
        col = mix(col, silkShade(rd, h.x, travel + h.x, h.yz, b), alpha);
    }

    return vec4(col, zr);
}

void main() {
    vec2 p = screenP();
    gSilkSwell = silkSwell();
    gSilkFlow = silkFlow();
    // The other parts of the music, each an average over many bands so it is
    // smooth across the spectrum, and shaped so a hat or a clap reads clearly.
    float bm = 0.0, bh = 0.0;
    for (int i = 0; i < 28; i++) bm += uBands[44 + i];    // ~0.8-3.5 kHz
    for (int i = 0; i < 16; i++) bh += uBands[80 + i];    // ~6-16 kHz
    gSilkMid = smoothstep(0.065, 0.11, sqrt(bm / 28.0)) * uActive;
    gSilkHi = smoothstep(0.03, 0.09, sqrt(bh / 16.0)) * uActive;
    gSilkSway = vec4(0.45 * sin(gSilkFlow * 0.23), 0.7 * sin(gSilkFlow * 0.31 + 1.0),
                     0.5 * sin(gSilkFlow * 0.19 + 2.0), 0.55 * sin(gSilkFlow * 0.27 + 3.0));
    gSilkW = 0.47 * (1.0 + 0.06 * gSilkSwell);
    gSilkH = 0.34 * (1.0 + 0.06 * gSilkSwell);
    gSilkLv = smoothstep(0.0, 0.18, uLevelSmooth);
    gSilkBs = smoothstep(0.0, 0.4, uBassSmooth);
    p = rot(0.06 * sin(uTime * 0.05)) * p;
    float travel = silkTravel();
    // The flight steers: the corridor leans toward the pointer.
    vec2 rd = p * 1.25 + vec2(0.04 * sin(uTime * 0.11), 0.03 * sin(uTime * 0.083 + 1.0))
             + pointerAt() * 0.08 * uMouseHere;

    vec4 sc0 = silkScene(p, rd, travel);
    vec3 col = sc0.rgb;

    // Glass blocks: the nearest one in front of the satin on this ray.
    int gi = -1;
    float gt = sc0.w;
    vec3 gc = vec3(0.0), ghs = vec3(1.0);
    vec2 gang = vec2(0.0);
    float gfade = 0.0;
    for (int i = 0; i < kSilkGlass; i++) {
        vec3 c, hs; vec2 ang; float fade;
        silkBlock(i, travel, c, hs, ang, fade);
        if (fade <= 0.0) continue;
        vec3 ro, d;
        vec2 tt = silkSlab(vec3(rd, 1.0), c, hs, ang, ro, d);
        if (tt.x > tt.y || tt.y < 0.0 || tt.x > gt) continue;
        gi = i; gt = tt.x; gc = c; ghs = hs; gang = ang; gfade = fade;
    }
    if (gi >= 0) col = silkGlassShade(col, p, vec3(rd, 1.0), travel, gi, gc, ghs, gang, gfade);

    // Beat sparks: pink and cyan glitter drifting outward from each beat,
    // fading in over the swell's rise and out over about a second. The last
    // two beats' bursts overlap so nothing pops at the beat.
    float ang = atan(p.y, p.x) * 7.0;
    float pr = length(p);
    for (int j = 0; j < 2; j++) {
        float fj = float(j);
        float cellA = floor(ang + fj * 0.5) + fj * 50.0;
        float a2 = (floor(ang + fj * 0.5) + 0.5 - fj * 0.5 + 0.6 * (hash11(cellA * 2.1) - 0.5)) / 7.0;
        vec2 dir = vec2(cos(a2), sin(a2));
        vec3 sc = mix(silkPink, silkCyan, hash11(cellA * 1.3));
        for (int k = 0; k < 2; k++) {
            float bk = floor(uBeats) - float(k);
            float x = uBeats - bk;                      // time since that beat, in beats
            float hs = hash11(cellA + bk * 1.37);
            float rr = 0.07 + (1.0 - exp(-x * 1.1)) * (0.12 + 0.3 * hs);   // eased outward drift, like glitter in slow air
            if (hs < 0.5 || abs(pr - rr) > 0.02) continue;
            float spark = 1.0 - smoothstep(0.002, 0.006 + 0.004 * hs + 1.5 / uResolution.y, length(p - dir * rr));
            col += sc * spark * silkSwellAt(x) * uActive * 1.4;
        }
    }

    col *= 1.0 - 0.25 * dot(p, p);                  // vignette
    col = mix(col * 0.5, col, smoothstep(0.0, 1.0, uActive));   // dims and settles in silence, easing
    fragColor = vec4(col, 1.0);
}