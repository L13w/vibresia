uniform vec3 uPalette[3];

void main() {
    vec2 p = screenP();
    vec3 c = render(p, uTime);
    fragColor = vec4(clamp(c, vec3(0.0), vec3(1.0)), 1.0);
}