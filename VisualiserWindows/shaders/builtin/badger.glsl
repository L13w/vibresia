void badgerAt(inout vec3 col, vec2 p, vec2 pos, float s, float ph) {
    vec2 q = (p - pos) / s;
    float w = aaw(s);
    float d = dance(ph);
    q.y += d * 0.1 * groove();               // squat down on the beat
    vec3 fur = vec3(0.42, 0.42, 0.46), dark = vec3(0.1), white = vec3(0.96);

    // arms pump up and down
    float up = d * uActive;                  // arms rest down in silence
    vec2 hl = mix(vec2(-0.3, -0.22), vec2(-0.33, 0.36), up);
    vec2 hr = mix(vec2(0.3, -0.22), vec2(0.33, 0.36), up);
    col = mix(col, dark * 2.2, fill(sdSeg(q, vec2(-0.18, 0.02), hl) - 0.055, w));
    col = mix(col, dark * 2.2, fill(sdSeg(q, vec2(0.18, 0.02), hr) - 0.055, w));

    col = mix(col, fur, fill(sdEllipse(q - vec2(0.0, -0.18), vec2(0.25, 0.3)), w));
    col = mix(col, fur * 0.72, fill(sdEllipse(q - vec2(0.0, -0.25), vec2(0.14, 0.19)), w));
    col = mix(col, dark, fill(sdCircle(vec2(abs(q.x), q.y) - vec2(0.14, 0.35), 0.055), w));

    // white head with the two black stripes running back over the eyes
    vec2 hq = q - vec2(0.0, 0.2);
    vec3 hc = white;
    float stripe = max(abs(abs(hq.x) - 0.085) - 0.038, -hq.y - 0.05);
    hc = mix(hc, dark, fill(stripe, w));
    col = mix(col, hc, fill(sdEllipse(hq, vec2(0.19, 0.17)), w));
    col = mix(col, white, fill(sdCircle(vec2(abs(hq.x), hq.y) - vec2(0.085, 0.0), 0.017), w));
    col = mix(col, dark, fill(sdEllipse(hq - vec2(0.0, -0.12), vec2(0.04, 0.028)), w));
}

void mushroomAt(inout vec3 col, vec2 p, float s) {
    vec2 q = p / s;
    float w = aaw(s);
    col = mix(col, vec3(0.97, 0.93, 0.82), fill(sdRoundBox(q - vec2(0.0, -0.17), vec2(0.12, 0.2), 0.06), w));
    float cap = max(sdEllipse(q - vec2(0.0, 0.0), vec2(0.4, 0.32)), -(q.y + 0.02));
    col = mix(col, vec3(0.86, 0.1, 0.12), fill(cap, w));
    for (int k = 0; k < 5; k++) {
        vec2 c = vec2(-0.24 + 0.12 * float(k), 0.1 + 0.07 * sin(float(k) * 2.3));
        col = mix(col, vec3(1.0), fill(sdCircle(q - c, 0.045), w) * fill(cap, w));
    }
}

void snakeAt(inout vec3 col, vec2 p, float rise) {
    float w = aaw(1.0);
    float headY = -0.5 + rise * (0.62 + uBass * 0.08);
    float sway = 0.07 * sin(p.y * 11.0 - uTime * 6.0);
    float body = max(abs(p.x - sway) - 0.045, p.y - headY);
    vec3 green = vec3(0.25, 0.7, 0.2);
    // zigzag markings along the body
    float marks = step(0.5, fract(p.y * 10.0 + (p.x - sway) * 6.0));
    col = mix(col, mix(green, green * 0.6, marks), fill(body, w));
    vec2 hp = vec2(0.07 * sin(headY * 11.0 - uTime * 6.0), headY);
    vec2 hq = p - hp;
    // flicking forked tongue
    float tl = 0.06 + 0.04 * step(0.5, fract(uTime * 3.0));
    float tongue = min(sdSeg(hq, vec2(0.0, 0.0), vec2(0.0, -tl - 0.04)),
                       sdSeg(hq, vec2(0.0, -tl - 0.04), vec2(0.02 * sign(hq.x), -tl - 0.07)));
    col = mix(col, vec3(0.9, 0.1, 0.15), fill(tongue - 0.006, w));
    col = mix(col, green * 1.1, fill(sdEllipse(hq, vec2(0.08, 0.06)), w));
    col = mix(col, vec3(1.0), fill(sdCircle(vec2(abs(hq.x), hq.y) - vec2(0.035, 0.015), 0.016), w));
    col = mix(col, vec3(0.0), fill(sdCircle(vec2(abs(hq.x), hq.y) - vec2(0.035, 0.015), 0.008), w));
}

uniform float uEventKind;   // 0 none, 1 mushroom, 2 snake (set on big beats)
uniform float uEventAge;    // seconds since the event started

void main() {
    vec2 p = screenP();
    float w = aaw(1.0);

    vec3 col = mix(vec3(0.6, 0.85, 1.0), vec3(0.25, 0.5, 0.95), p.y + 0.5);
    col = mix(col, vec3(1.0, 0.93, 0.5), fill(sdCircle(p - vec2(0.62, 0.32), 0.07 + uBass * 0.03), w));
    col = mix(col, vec3(0.38, 0.72, 0.32), fill(p.y - (-0.02 + 0.04 * sin(p.x * 3.0)), w));

    for (int r = 0; r < 3; r++) {
        float fr = float(r);
        float s = 0.2 + fr * 0.06;
        float y = 0.0 - fr * 0.17;
        int n = 5 - r;
        for (int i = 0; i < 5; i++) {
            if (i >= n) break;
            float x = (float(i) - float(n - 1) * 0.5) * (0.4 + fr * 0.12);
            badgerAt(col, p, vec2(x, y), s, float(i) * 1.3 + fr * 0.7);
        }
        float grassTop = y - s * 0.32 + 0.012 * sin(p.x * 70.0 + fr);
        col = mix(col, vec3(0.27 + 0.05 * fr, 0.6 + 0.05 * fr, 0.24), fill(p.y - grassTop, w));
    }

    // Big-beat overlays, drawn over the badgers: pop in fast, hold, drop away.
    float env = smoothstep(0.0, 0.15, uEventAge) * (1.0 - smoothstep(1.3, 1.6, uEventAge));
    if (uEventKind > 0.5 && uEventKind < 1.5) {
        col = mix(col, vec3(1.0, 0.95, 0.97), 0.2 * env);
        // The mushroom comes up where you are pointing.
        mushroomAt(col, p - vec2(pointerAt().x * uMouseHere, -0.08 - (1.0 - env) * 0.5),
                   0.55 + uBass * 0.1 + uBeatStrength * 0.1);
    } else if (uEventKind > 1.5) {
        snakeAt(col, p, env);
    }
    fragColor = vec4(col, 1.0);
}