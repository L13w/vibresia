void main() {
    vec2 p = screenP();
    p *= 1.15 - uBass * 0.3 - uBeatStrength * 0.15;     // punch in on the bass
    // The centre of the folds follows the pointer: the kaleidoscope is
    // pointed at whatever you are pointing at.
    vec2 pc = p - pointerAt() * 0.12 * uMouseHere;
    float r = length(pc);
    float a = atan(pc.y, pc.x) + uTime * 0.12;
    float segs = 8.0;
    float seg = 2.0 * PI / segs;
    a = mod(a, seg);
    a = abs(a - seg * 0.5);
    vec2 k = vec2(cos(a), sin(a)) * r;
    k += vec2(0.35 + 0.1 * sin(uTime * 0.21), 0.2 * cos(uTime * 0.17));

    // kaliset fractal folds
    float acc = 0.0;
    float c = 0.78 + 0.12 * uMid;
    for (int i = 0; i < 9; i++) {
        k = abs(k) / dot(k, k) - c;
        acc += exp(-abs(length(k) - 0.6) * 6.0);
    }
    acc /= 9.0;
    // hue walks a full cycle every 16 beats
    vec3 col = 0.5 + 0.5 * cos(PI * 2.0 * (acc * 1.4 + uBeats / 16.0 + vec3(0.0, 0.33, 0.67)));
    col *= 0.25 + acc * (1.4 + uLevel * 1.2);
    col *= 0.8 + 1.2 * bandAt(clamp(r, 0.0, 1.0));       // rings glow with their band
    col += vec3(1.0, 0.8, 1.0) * uBeatStrength * 0.25 * exp(-r * 4.0);
    col *= smoothstep(1.2, 0.2, r);
    col = mix(vec3(0.05, 0.02, 0.08), col, uActive);   // flat single colour in silence
    fragColor = vec4(col, 1.0);
}