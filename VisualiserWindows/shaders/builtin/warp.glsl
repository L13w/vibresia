void main() {
    // The stars flow toward the pointer: the centre of the field moves
    // to it, so the streaks run where you are looking.
    vec2 p = screenP() - pointerAt() * 0.12 * uMouseHere;
    float r = length(p);
    float a = atan(p.y, p.x);
    vec3 col = vec3(0.01, 0.0, 0.03);
    float px = 1.5 / uResolution.y;

    for (int layer = 0; layer < 3; layer++) {
        float fl = float(layer);
        float N = 90.0 + fl * 40.0;
        float slot = floor((a + PI) / (2.0 * PI) * N);
        float h = hash11(slot * 1.37 + fl * 91.0);
        float ac = (slot + 0.5) / N * 2.0 * PI - PI;
        float z = fract(h * 7.31 + uTime * (0.22 + 0.08 * fl));
        float sr = z * z * 1.3 + 0.02;                     // accelerates outward
        float len = 0.01 + (0.04 + uLevel * 0.4 + uBeatStrength * 0.2) * z;
        float da = abs(a - ac) * r;
        float along = step(sr - len, r) * step(r, sr);
        float fade = smoothstep(sr - len, sr, r);
        float width = px * (0.6 + z * 2.5);
        float s = (1.0 - smoothstep(0.0, width, da)) * along * fade * z;
        // more stars when louder, none in silence
        s *= step(h, uActive * (0.15 + 0.85 * clamp(uLevel * 1.8, 0.0, 1.0)));
        vec3 tint = mix(vec3(0.7, 0.85, 1.0), vec3(1.0, 0.5, 0.9), clamp(uTreble * 2.0, 0.0, 1.0) * h);
        col += tint * s * 1.6;
    }
    // core glow and a shock ring on beats
    col += vec3(0.5, 0.3, 1.0) * exp(-r * 9.0) * (0.3 + uBass + uBeatStrength) * uActive;
    float ringR = (1.0 - uBeatStrength) * 0.9;
    col += vec3(0.8, 0.6, 1.0) * uBeatStrength * (1.0 - smoothstep(0.0, 0.015, abs(r - ringR))) * 0.6;
    fragColor = vec4(col, 1.0);
}