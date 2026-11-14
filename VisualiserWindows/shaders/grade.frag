#version 330 core
in vec2 vUV;
uniform sampler2D uTex;
uniform vec3 uShadow;
uniform vec3 uHighlight;
uniform float uSaturation;
uniform float uDuotone;
uniform float uLift;
out vec4 fragColor;

void main() {
    vec3 c = texture(uTex, vUV).rgb;
    float luma = dot(c, vec3(0.2126, 0.7152, 0.0722));

    // Duotone: stretch the luminance ramp between the two palette colours.
    vec3 graded = uShadow + (uHighlight - uShadow) * luma;
    c = mix(c, graded, uDuotone);

    // Saturation, around the graded luminance.
    luma = dot(c, vec3(0.2126, 0.7152, 0.0722));
    c = mix(vec3(luma), c, uSaturation);

    // Lift the blacks toward the shadow colour so the image doesn't crush.
    c += uShadow * uLift * (1.0 - luma);

    fragColor = vec4(clamp(c, 0.0, 1.0), 1.0);
}