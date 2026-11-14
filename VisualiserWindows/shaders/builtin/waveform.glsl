// Spectrum at x (0..1), interpolated between bands so it has no steps.
float spectrumAt(float x01) {
    float f = clamp(x01, 0.0, 1.0) * 95.0;
    int i = int(floor(f));
    float t = fract(f);
    return mix(uBands[i], uBands[min(i + 1, 95)], t * t * (3.0 - 2.0 * t));
}

float wave(float x, float aspect, int i) {
    float fi = float(i);
    float x01 = x / aspect + 0.5;
    float edge = smoothstep(0.0, 0.12, x01) * smoothstep(1.0, 0.88, x01);
    float amp = (0.04 + 0.38 * spectrumAt(x01) * (1.0 - fi * 0.09)) * edge * uActive;
    return amp * sin(x * (2.5 + fi * 1.9) * PI + uScroll * (3.0 + fi * 1.4) + fi * 1.7)
               * (0.65 + 0.35 * sin(x * 1.7 - uScroll * 0.8 + fi * 2.3));
}

void main() {
    vec2 p = screenP();
    float aspect = uResolution.x / uResolution.y;

    vec3 col = vec3(0.0, 0.025, 0.015);
    // faint HUD grid and centre line
    vec2 g = abs(fract(p * 10.0) - 0.5);
    col += vec3(0.0, 0.10, 0.05) * (1.0 - smoothstep(0.0, 0.03, min(g.x, g.y))) * 0.5;
    col += vec3(0.05, 0.35, 0.15) * (1.0 - smoothstep(0.0, 0.0025, abs(p.y))) * 0.6;

    float halo = 0.0, core = 0.0;
    const float eps = 0.002;
    for (int i = 0; i < 7; i++) {
        // The pointer drags the trace up and down, the way you would
        // with a scope's position control.
        float y = wave(p.x, aspect, i) + pointerAt().y * 0.10 * uMouseHere;
        float slope = (wave(p.x + eps, aspect, i) - y) / eps;
        float d = abs(p.y - y) / sqrt(1.0 + slope * slope);   // distance to the curve, not just vertical
        float w = 0.0012 + 0.0006 * float(i);
        float weight = 1.0 - float(i) / 8.0;
        halo += weight * 0.012 / (d + 0.012);
        core += weight * (1.0 - smoothstep(0.0, w * 2.0, d));
    }
    float flare = 0.7 + uBeatStrength * 0.6 * uActive;
    vec3 green = vec3(0.15, 1.0, 0.45);
    col += green * halo * 0.07 * flare;
    col += mix(green, vec3(0.75, 1.0, 0.8), 0.3) * core * flare;
    fragColor = vec4(min(col, vec3(1.0)), 1.0);
}