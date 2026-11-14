// Flux: an abstract long-exposure light sweep. Huge fanned sheets of fine
// light strands, hot pink and molten gold, sweep in from the edges of the
// frame and swirl into a drifting vanishing point, like light trails seen
// while flying. Gold flakes and bokeh and a few cyan glass shards streak out
// past the camera through black space. It flies faster, streaks longer and
// burns brighter when the music is loud, and every motion glides.
// In silence it rests: dim, soft and slowly drifting.

const vec3 fluxPink = vec3(1.0, 0.12, 0.42);
const vec3 fluxGold = vec3(1.0, 0.52, 0.06);
const vec3 fluxCyan = vec3(0.0, 1.0, 0.80);

float fluxSq(float x) { return x * x; }

// Cheap 1D value noise for things that only vary along a strand.
float fluxNoise1(float x) {
    float i = floor(x), f = fract(x);
    return mix(hash21(vec2(i, 0.37)), hash21(vec2(i + 1.0, 0.37)), f * f * (3.0 - 2.0 * f));
}

// A cheap atan2 (error under 0.01 rad), good enough for drawing.
float fluxAtan(float y, float x) {
    float ax = abs(x), ay = abs(y);
    float a = min(ax, ay) / max(max(ax, ay), 1e-6);
    float s = a * a;
    float r = ((-0.0464964749 * s + 0.15931422) * s - 0.327622764) * s * a + a;
    if (ay > ax) r = 1.57079637 - r;
    if (x < 0.0) r = PI - r;
    return y < 0.0 ? -r : r;
}

// A soft swell that peaks on each beat and eases away; never a kick.
float fluxSwell() { return (0.5 + 0.5 * cos(2.0 * PI * uBeats)) * uActive; }

// How much the music is playing: 0 in silence, 1 with sound. uPresence is
// the renderer's slow music presence (a critically damped ease, ~2.3 s each
// way, from the eased loudness, so it never snaps or chatters); the ends are
// trimmed so it lands on exactly 0 and 1.
float fluxPlay() { return smoothstep(0.02, 0.98, uPresence); }
float gPlay;      // fluxPlay(), once per pixel
float gIdle;      // 1 - gPlay

// One sheet of light strands. `th` is the swirled angle round the vanishing
// point, `r` the distance from it and `lr` its log, so equal steps of `lr`
// fly past at equal speed. a0: the sheet's centre angle, w: its half-width
// in angle, n: how many strands across it. Returns the colour in rgb and how
// much it covers what's behind in a.
vec4 fluxSheet(float th, float r, float lr, float travel, float sm, float px,
               float a0, float w, float n, float seed, vec3 tint, float lit) {
    // in silence each sheet sways slowly open and shut (45 s, divides uClock's hour)
    a0 += gIdle * 0.16 * sin(uClock * (2.0 * PI / 45.0) + seed * 1.7);
    float d = th - a0;
    d -= 2.0 * PI * floor((d + PI) / (2.0 * PI));            // wrap to -PI..PI
    // the sheet ripples as it flies
    d += 0.14 * (fluxNoise1(lr * 1.6 - travel * 0.5 + seed * 11.0) - 0.5);
    // pinched towards the vanishing point, fanning out wide at the edges
    w *= 0.3 + 0.7 * smoothstep(0.0, 0.55, r);
    float x = d / w;                                          // -1..1 across
    float ax = abs(x);
    if (ax > 1.25) return vec4(0.0);
    // ragged, flickering edges, brighter than the middle
    float rag = fluxNoise1(lr * 2.5 - travel * 0.8 + seed * 5.0);
    float env = smoothstep(1.0 + 0.2 * rag, 0.7 - 0.2 * rag, ax);
    float rim = smoothstep(0.45, 0.9, ax) * env;
    // the strands: hairlines across the sheet, softened where they crowd
    // together towards the vanishing point so they never shimmer
    float c = (x * 0.5 + 0.5) * n + seed * 13.0;
    float fi = floor(c);
    float fh = hash21(vec2(fi, seed));
    float pxs = r * 2.0 * w / n * px;                        // pixels per strand
    float line = smoothstep(0.2 + 0.3 * fh, 0.0, abs(fract(c) - 0.5));
    line = mix(0.5, line, smoothstep(1.5, 4.0, pxs) * 0.6);
    // light streaks flying outward along each strand, stretched by the rush
    float st = fluxNoise1((lr * 10.0 / sm - travel * (2.0 + fh)) + fi * 7.31);
    st = smoothstep(0.2, 0.85, st);
    // chunky glitter caught in the sheet, smeared along the flight
    float gc = floor(c * 0.34);
    float gq = lr * 40.0 / sm - travel * 9.0 + hash21(vec2(gc, seed)) * 5.0;
    float gh = hash21(vec2(gc + seed * 17.0, floor(gq)));
    float grain = step(0.74, gh) * (0.4 + 2.2 * fluxSq(fract(gh * 7.3)))
                * smoothstep(0.5, 0.1, abs(fract(gq) - 0.5)) * smoothstep(1.2 + 1.2 * fract(gh * 3.1), 0.2, abs(fract(c * 0.34) - 0.5) * 3.4);
    // the light gathers and thins in broad smeared patches
    // ("patch" is a reserved word in GLSL ES, which is what a browser's
    // WebGL2 context speaks, so the name here is sheetPatch.)
    float sheetPatch = fluxNoise1(lr * 3.0 / sm - travel * 0.9 + floor(c * 0.1) * 3.7 + seed);
    float s = env * (0.1 + 0.35 * sheetPatch + line * (0.2 + 0.8 * st) * (0.35 + 0.7 * fh) * (0.4 + 0.9 * sheetPatch)) + rim * (0.25 + 0.6 * st);
    vec3 hot = mix(tint, vec3(1.0, 0.88, 0.72), 0.55);
    vec3 col = tint * s + hot * (grain * env * (0.6 + 1.6 * uTreble) + rim * line * st * 0.5 + fluxSq(st * sheetPatch) * env * 0.35);
    // the strands thin out into the vanishing point
    float depth = smoothstep(0.012, 0.2, r);
    return vec4(col * lit * depth, env * 0.55 * depth);
}

void main() {
    vec2 p = screenP();
    // Clocks: uTime runs at the eased activity, so it glides; uScroll adds a
    // little extra speed when it's loud. Both stop in silence and never run
    // backwards. The beat swell only touches brightness, never the motion.
    float clk = uTime * 0.5 + uScroll * 0.4;
    float swl = fluxSwell();
    float travel = uTime * 3.6 + uScroll * 2.6;               // forward flight
    // streaks stretch with the (eased) flight speed, so motion blur hides
    // the step from frame to frame
    // it wakes and settles with the music presence (~2.3 s each way)
    gPlay = fluxPlay();
    gIdle = 1.0 - gPlay;
    float sm = 1.0 + 2.0 * uActive * gPlay;
    float lit = (0.35 + 0.65 * gPlay) * (1.05 + (0.8 * uLevel + 0.15 * uBass + 0.1 * swl) * gPlay);
    float breath = 0.5 + 0.5 * sin(uClock * (2.0 * PI / 8.0));   // 8 s, divides uClock's hour
    float px = uResolution.y;

    // the vanishing point drifts slowly round the middle; the view rolls
    // the vanishing point circles slowly round the middle (a circle never
    // reverses), and the camera leans in as the flight eases up
    // The eye of the storm follows the pointer.
    vec2 v = vec2(-0.05, 0.03) + vec2(0.09 * cos(clk * 0.12), 0.055 * sin(clk * 0.12))
           + pointerAt() * 0.10 * uMouseHere;
    vec2 q = (p - v) / (1.0 + 0.06 * uActive * gPlay);
    float r = max(length(q), 1e-4);
    float lr = log(r);
    float th = fluxAtan(q.y, q.x) + 0.22 * clk + uClock * (2.0 * PI / 600.0);   // a slow, steady roll that never quite stops
    // the sheets swirl harder the nearer they get to the vanishing point
    float sw = 1.0 / (r * 5.0 + 0.45) * mix(0.6, 1.0, gPlay);   // relaxes into long curves in silence

    vec3 col = vec3(0.004, 0.03, 0.0);                        // lands near-neutral black after the grade
    // gold sheets underneath, pink over them
    vec4 s;
    // a broad molten-gold sheet sweeping up from the bottom right, a thin
    // gold one tucked under the pink, a huge pink sheet wrapping in from the
    // top left and a smaller pink one from the bottom left
    s = fluxSheet(th + 1.4 * sw, r, lr, travel, sm, px, -0.95 + 0.3 * sin(clk * 0.05), 1.05, 170.0, 1.0, fluxGold, lit * 1.35);
    col = col * (1.0 - s.a) + s.rgb;
    s = fluxSheet(th + 1.1 * sw, r, lr, travel * 1.1, sm, px, 1.75 + 0.15 * sin(clk * 0.04 + 1.0), 0.3, 60.0, 2.0, fluxGold, lit * 1.2);
    col = col * (1.0 - s.a) + s.rgb;
    s = fluxSheet(th + 1.7 * sw, r, lr, travel * 0.95, sm, px, 2.45 + 0.3 * sin(clk * 0.045 + 2.0), 1.0, 170.0, 3.0, fluxPink, lit * 1.35);
    col = col * (1.0 - s.a) + s.rgb;
    s = fluxSheet(th + 1.2 * sw, r, lr, travel * 1.05, sm, px, -2.35 + 0.3 * sin(clk * 0.05 + 3.0), 0.42, 90.0, 4.0, fluxPink, lit * 1.2);
    col = col * (1.0 - s.a) + s.rgb;

    // a hot swirl of light right in the vanishing point
    col += mix(fluxPink, fluxGold, 0.55) * exp(-r * 14.0) * (0.3 + 0.25 * swl) * lit;

    // gold flakes, bokeh and cyan glass rushing out past the camera: far
    // layers are fine specks, near ones big soft discs, each smeared along
    // its flight line
    vec2 rdir = q / r;
    float busy = 0.35 + 0.9 * exp(-r * 3.0);
    for (int k = 0; k < 4; k++) {
        float fk = float(k);
        float z = fract(fk * 0.25 + travel * 0.1 + uClock / 40.0);   // 0 far, 1 right past us; drifts on in silence
        float scale = mix(28.0, 1.6, z * z);
        vec2 bq = q * scale + vec2(fk * 17.3, fk * 9.1);
        vec2 bc = floor(bq);
        float bh = hash21(bc + fk * 3.1);
        if (bh > 0.06 + 0.4 * busy) continue;                // an empty patch of space
        float h1 = hash21(bc + 1.3), h2 = fract(h1 * 37.1), h3 = fract(h1 * 91.7), h4 = fract(h1 * 13.3);
        vec2 lo = fract(bq) - 0.5 - (vec2(h1, h2) - 0.5) * 0.3;
        float along = dot(lo, rdir), across = dot(lo, vec2(-rdir.y, rdir.x));
        float fade = smoothstep(0.0, 0.25, z) * smoothstep(1.0, 0.93, z);
        float size = (0.04 + 0.16 * h3 * h3) * (0.6 + 0.9 * z);
        // motion streak along the flight line, kept inside the cell
        float blur = min(1.0 + (0.8 + 5.0 * z * z) * uActive * gPlay, 0.44 / size);
        float spin = bh * 300.0 + clk * (1.0 + 3.0 * h4) + travel * 0.6 + uClock * (2.0 * PI / 60.0);
        vec2 cp = rot(spin) * vec2(along / blur, across);
        if (bh < 0.012 && z > 0.4) {
            // a cyan glass shard: a skewed slab with a corner sheared off,
            // flipping end over end
            cp.x /= max(abs(cos(spin * 0.7 + h2 * 9.0)), 0.25);
            vec2 kp = vec2(cp.x + (h2 - 0.5) * cp.y, cp.y);
            vec2 cut = normalize(vec2(h3 - 0.5, h4 - 0.5) + 0.01);
            float sd = max(sdBox(kp, size * vec2(0.9 + 0.6 * h4, 0.5 + 0.4 * h2)), dot(cp, cut) - size * (0.2 + 0.3 * h1));
            float edge = 0.012 + 0.1 * z * z;
            float shard = smoothstep(edge, -edge, sd);
            float rimg = smoothstep(edge + 0.03, 0.0, abs(sd));
            float face = smoothstep(-0.1, 0.1, cp.x - cp.y);
            vec3 glass = fluxCyan * (0.3 + 0.4 * face + 0.7 * rimg) * (1.0 + 0.8 * uTreble + 0.4 * swl) * (0.35 + 0.65 * uActive);
            col = mix(col, glass + col * 0.4, shard * fade * 0.75);
        } else {
            // a gold speck far off, a big soft bokeh disc up close
            float soft = mix(0.03, 0.8, z * z) * size + 0.008;
            float dd = length(cp * vec2(1.0, 1.0 + 0.5 * h2)) - size;
            float flake = smoothstep(soft, -soft, dd);
            vec3 fc = mix(vec3(1.0, 0.45, 0.1), vec3(1.0, 0.82, 0.55), h4);
            float glint = fluxSq(fluxSq(max(cos(spin * 1.3), 0.0)));
            float fb = (0.4 + 1.0 * h2) * mix(1.5, 0.85, z) * lit * fade / sqrt(blur);
            col += fc * flake * fb * (0.6 + 0.5 * glint + 0.8 * uTreble * (1.0 - z));
        }
    }

    col = 1.0 - exp(-col * 1.5);                      // soft highlight roll-off
    // in silence: a soft, slightly cooler glow that breathes every 8 s
    vec3 rest = pow(col, vec3(0.8)) * (0.5 + 0.08 * breath);
    rest = mix(rest, rest * vec3(0.8, 0.95, 1.3), 0.7);
    rest += mix(fluxPink, fluxGold, 0.4) * exp(-r * 3.0) * (0.035 + 0.02 * breath);
    col = mix(rest, col, gPlay);
    fragColor = vec4(col, 1.0);
}