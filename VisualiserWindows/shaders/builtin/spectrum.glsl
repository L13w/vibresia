uniform float uPeaks[48];   // per-bar peak hold, decayed on the CPU

void main() {
    vec2 uv = vUV;
    vec3 col = vec3(0.02, 0.02, 0.04) + vec3(0.08, 0.02, 0.1) * uBeatStrength;
    // faint grid
    vec2 gp = gl_FragCoord.xy / 24.0;
    float grid = max(step(0.95, fract(gp.x)), step(0.95, fract(gp.y)));
    col += vec3(0.03, 0.03, 0.06) * grid;

    const float BARS = 48.0;
    // The analyser is tuned to where you are pointing: the band under the
    // pointer is marked, the way a bench analyser is.
    float mBand = floor(pointerUV().x * BARS);
    float peak = uPeaks[int(min(floor(uv.x * BARS), BARS - 1.0))] * (1.0 - 0.18 - 0.06);
    const float SEGS = 28.0;
    float base = 0.18;
    float bi = floor(uv.x * BARS);
    float bx = fract(uv.x * BARS);
    float v = max(bandAt(bi / BARS), bandAt((bi + 0.5) / BARS));
    float h = v * (1.0 - base - 0.06);
    if (bx > 0.12 && bx < 0.88) {
        float y = uv.y - base;
        float seg = floor(y / (1.0 / SEGS));
        float sy = fract(y * SEGS);
        float t = seg / SEGS / (1.0 - base);
        vec3 led = t < 0.55 ? mix(vec3(0.1, 1.0, 0.3), vec3(0.9, 1.0, 0.1), t / 0.55)
                            : mix(vec3(0.9, 1.0, 0.1), vec3(1.0, 0.15, 0.1), (t - 0.55) / 0.45);
        if (y >= 0.0 && sy < 0.75) {
            float lit = step(seg / SEGS, h);
            // The pointed-at band is brighter.
            float focused = (abs(bi - mBand) < 0.5) ? 1.0 : 0.0;
            col = mix(col, led * (0.12 + 0.88 * lit) * (1.0 + 0.9 * focused * uMouseHere), 1.0);
            // falling peak cap
            if (peak > 0.01 && seg == floor(peak * SEGS)) col = vec3(1.0);
        }
        // reflection
        float ry = base - uv.y;
        if (ry > 0.0 && ry < h * 0.5 && fract(ry * SEGS * 2.0) < 0.75) {
            col = mix(col, led * 0.6, 0.35 * (1.0 - ry / (h * 0.5 + 1e-4)));
        }
    }
    // The cursor: a line up the display where you are pointing, the way
    // a bench analyser has a marker you slide across it.
    float cursor = 1.0 - smoothstep(0.0, 0.006, abs(uv.x - pointerUV().x));
    col += vec3(0.35, 1.0, 0.55) * cursor * 0.45 * uMouseHere;
    fragColor = vec4(col, 1.0);
}