void llamaAt(inout vec3 col, vec2 p, vec2 pos, float s, float ph, float seed) {
    vec2 q = (p - pos) / s;
    float w = aaw(s);
    float d = dance(ph);
    float g = groove();
    q.y -= abs(sin(uBeats * PI + ph * 0.35)) * 0.08 * g;   // hop on each beat
    vec3 wool = mix(vec3(0.97, 0.94, 0.88), vec3(0.72, 0.5, 0.32), step(0.6, seed));
    vec3 legC = wool * 0.8;

    // legs: the front pair kicks
    float kick = (d - 0.5) * 0.25 * g;
    float legs = min(min(sdSeg(q, vec2(-0.18, -0.1), vec2(-0.2, -0.42)),
                         sdSeg(q, vec2(-0.08, -0.1), vec2(-0.1, -0.42))),
                     min(sdSeg(q, vec2(0.1, -0.1), vec2(0.12 + kick, -0.42 + abs(kick) * 0.4)),
                         sdSeg(q, vec2(0.2, -0.1), vec2(0.22 - kick, -0.42 + abs(kick) * 0.4))));
    col = mix(col, legC, fill(legs - 0.035, w));

    // fluffy body with a striped blanket
    float body = sdEllipse(q - vec2(0.0, -0.04), vec2(0.3, 0.15));
    body = min(body, sdCircle(q - vec2(-0.3, 0.02), 0.05));   // tail
    col = mix(col, wool, fill(body, w));
    vec3 blanket = 0.5 + 0.5 * cos(PI * 2.0 * (floor(q.x * 14.0) * 0.17 + seed + vec3(0.0, 0.33, 0.67)));
    float bl = max(sdBox(q - vec2(-0.02, 0.03), vec2(0.16, 0.07)), body);
    col = mix(col, blanket, fill(bl, w));

    // swaying neck and head
    float sway = (d - 0.5) * 0.14 * g;
    vec2 top = vec2(0.24 + sway, 0.36);
    col = mix(col, wool, fill(sdSeg(q, vec2(0.2, 0.0), top) - 0.065, w));
    vec2 hq = q - top;
    col = mix(col, wool, fill(sdEllipse(hq - vec2(0.05, 0.0), vec2(0.11, 0.07)), w));
    vec2 eq = vec2(abs(hq.x + 0.01), hq.y) - vec2(0.04, 0.1);
    col = mix(col, wool * 0.85, fill(sdEllipse(rot(0.25) * eq, vec2(0.022, 0.06)), w));
    col = mix(col, vec3(0.05), fill(sdCircle(hq - vec2(0.04, 0.025), 0.014), w));

    // the whoop: mouth opens and sound rings burst out on beats
    float whoop = smoothstep(0.35, 0.8, uBeatStrength);
    col = mix(col, vec3(0.25, 0.05, 0.08), fill(sdEllipse(hq - vec2(0.14, -0.03), vec2(0.03, 0.005 + 0.03 * whoop)), w));
    // The llama you are pointing at is the one that calls at you: how
    // near the pointer is. Measured on the screen, where the pointer is.
    vec2 toMouse = pointerAt() - pos;
    float near = exp(-dot(toMouse, toMouse) * 7.0) * uMouseHere;
    // Its head turns toward the pointer, so the call comes from that one
    // rather than from all of them.
    float turn = clamp(atan(toMouse.y, toMouse.x), -0.8, 0.8) * near;
    vec2 hqTurned = rot(turn) * hq;
    // and it calls harder for you
    float call = whoop + near * 0.6;
    col = mix(col, vec3(0.25, 0.05, 0.08), fill(sdEllipse(hqTurned - vec2(0.14, -0.03), vec2(0.03, 0.005 + 0.055 * call)), w));
    vec2 mouth = vec2(0.16, -0.03);
    float ringR = 0.06 + (1.0 - uBeatStrength) * 0.3 + near * 0.14;
    float ring = abs(sdCircle(hqTurned - mouth, ringR)) - 0.008;
    ring = max(ring, -(hqTurned.x - 0.16));  // only the half in front of the mouth
    col = mix(col, vec3(1.0, 0.95, 0.6), fill(ring, w) * clamp(call, 0.0, 1.0));
}

void main() {
    vec2 p = screenP();
    float w = aaw(1.0);
    // Andes sunset
    vec3 col = mix(vec3(1.0, 0.6, 0.35), vec3(0.35, 0.15, 0.5), p.y + 0.5);
    col = mix(col, vec3(1.0, 0.85, 0.5), fill(sdCircle(p - vec2(-0.45, 0.05), 0.1 + uBass * 0.04), w));
    float far = p.y - (0.02 + 0.12 * abs(fract(p.x * 1.3 + 0.2) - 0.5) * 2.0 * (0.6 + 0.4 * sin(p.x * 5.0)));
    col = mix(col, vec3(0.45, 0.25, 0.55), fill(far, w));
    float snow = max(far + 0.06, -(far + 0.0));
    col = mix(col, vec3(0.95, 0.9, 1.0), fill(snow, w) * step(0.09, p.y));
    float near = p.y - (-0.12 + 0.05 * sin(p.x * 4.0 + 1.0));
    col = mix(col, vec3(0.3, 0.55, 0.3), fill(near, w));

    for (int r = 0; r < 2; r++) {
        float fr = float(r);
        float s = 0.22 + fr * 0.08;
        float y = -0.05 - fr * 0.2;
        int n = 4 - r;
        for (int i = 0; i < 4; i++) {
            if (i >= n) break;
            float x = (float(i) - float(n - 1) * 0.5) * (0.42 + fr * 0.12);
            float seed = hash11(float(i) + fr * 10.0);
            llamaAt(col, p, vec2(x, y), s, float(i) * 1.7 + fr * 0.9, seed);
        }
    }
    fragColor = vec4(col, 1.0);
}