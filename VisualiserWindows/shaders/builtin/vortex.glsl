void vortexAt(inout vec3 col, vec2 p, float t) {
    float r = length(p);
    float a = atan(p.y, p.x);

    // Speed advances with uClock; bass causes dynamic FOV expansion
    float speed = uClock * 1.5;
    float fovPulse = 1.0 + uBassSmooth * 0.4 + uBeat * 0.2;

    vec2 uv;
    uv.x = a / (2.0 * 3.14159265) + 0.5;
    uv.y = (0.25 * fovPulse) / (r + 0.01) + speed;

    // Bass-driven spiral twist
    uv.x += sin(uv.y * 2.0 + t) * (0.05 + uBass * 0.1);

    // Cyber grid geometry
    vec2 grid = abs(fract(uv * vec2(12.0, 24.0)) - 0.5);
    float gridLines = fill(max(grid.x, grid.y) - 0.45, aaw(1.0));

    // Cyber colors (cyan to magenta) with treble-reactive rings
    vec3 cyan = vec3(0.0, 0.85, 1.0);
    vec3 magenta = vec3(1.0, 0.15, 0.7);
    vec3 silver = vec3(0.8, 0.9, 1.0);

    float ring = pow(sin(uv.y * 31.4) * 0.5 + 0.5, 4.0) * (0.8 + uTreble * 1.5);

    vec3 c = vec3(0.01, 0.02, 0.08);
    c += mix(cyan, magenta, sin(uv.y * 3.0 + t) * 0.5 + 0.5) * gridLines;
    c += silver * ring * (1.0 / (r * 3.0 + 0.2));

    // Center glow pulsing with bass and beat strength
    c += vec3(0.6, 0.8, 1.0) * exp(-r * 8.0) * (1.0 + uBassSmooth * 2.0 + uBeatStrength);

    // Fade at outer edges and scale by silence factor
    c *= smoothstep(1.2, 0.3, r);
    col = c;
}

void main() {
    vec3 col = vec3(0.0);
    // The tunnel steers: its vanishing point moves to the pointer, so the
    // grid converges where you are looking.
    vortexAt(col, screenP() - pointerAt() * 0.14 * uMouseHere, uTime);
    fragColor = vec4(min(col * uActive, vec3(1.0)), 1.0);
}