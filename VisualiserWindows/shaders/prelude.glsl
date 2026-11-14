#version 330 core
in vec2 vUV;
uniform float uTime;
uniform float uBass;
uniform float uMid;
uniform float uTreble;
uniform float uBeat;
uniform float uBeatStrength;
uniform float uLevel;
uniform vec2 uResolution;
uniform float uIdle;
uniform float uBands[96];
uniform float uActive;      // 0 in silence, eases to 1 with sound
uniform float uBeats;       // beat counter at the detected tempo; continuous, never steps or runs back
uniform float uScroll;      // forward-only distance, faster when louder
uniform float uClock;       // wall-clock seconds; keeps running in silence (use sparingly)
uniform float uLevelSmooth; // slow loudness, 0..1: rises over ~0.3 s, falls over ~1.5 s
uniform float uBassSmooth;  // the same slow easing applied to uBass
uniform float uPresence;    // music presence, 0..1: eases in and out over ~2.3 s, no chatter
uniform int uGenre;         // the genre of the music: 0 classical, 1 electronic, 2 rock,
                             // 3 pop, 4 hiphop, 5 jazz, 6 blues, 7 country, 8 rnb, 9 ambient
uniform float uTrackPos;    // seconds into the track, 0 when there is no position to know
uniform float uTrackDuration; // the length of the track in seconds, 0 when unknown
uniform float uTrackProgress; // 0..1 through the track, 0 when unknown
uniform float uTrackKnown;  // 1 when the position is real, 0 when there is none to know
uniform vec2 uMouse;         // the pointer, in the same coordinates as p: x across, y up from the middle
uniform float uMouseDown;    // 1 while a mouse button is held
uniform float uMouseHere;    // 1 when there is a pointer to react to, 0 when there is not
uniform float uKey;          // the last key pressed, as its character code; 0 when nothing has been pressed
uniform float uKeyDown;      // 1 while a key is held
uniform float uMidiNote;     // the last MIDI note, 0..127
uniform float uMidiVelocity; // how hard it was hit, 0..1
uniform float uMidiNotes;    // how many MIDI notes are held right now
uniform float uMidiCC;       // the last MIDI controller that moved, 0..127
uniform float uMidiCCValue;  // where that controller is, 0..1
out vec4 fragColor;

const float PI = 3.14159265;

// Centered coords: y spans -0.5..0.5, x scales with aspect ratio.
vec2 screenP() { return (gl_FragCoord.xy - 0.5 * uResolution) / uResolution.y; }

// Where the pointer is, in the same coordinates as p: x across the
// screen, y up from the middle. It is eased on its way here, so it
// never teleports from one place to another.
vec2 pointerAt() { return uMouse; }

// Where the pointer is, in 0..1 screen coordinates, y up. For the
// vibescapes that draw in that space rather than in p.
vec2 pointerUV() {
    return vec2(0.5 + uMouse.x * uResolution.y / uResolution.x, 0.5 + uMouse.y);
}

// Anti-alias width for a shape drawn at scale `s`.
float aaw(float s) { return 1.5 / (uResolution.y * s); }

float hash11(float n) { return fract(sin(n * 127.1) * 43758.5453); }
float hash21(vec2 p) {
    p = fract(p * vec2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return fract(p.x * p.y);
}

// Smooth noise, and four octaves of it. This is what anything that looks
// like a cloud, a flame or smoke is built from — and it wasn't here, so
// everyone was writing their own, or reaching for sin() and wondering why
// the result looked like a gradient.
//
// The trap: scale matters more than anything else. Sampled at about the
// screen's scale these are too smooth to see — across a shape a few tenths
// wide you'd be looking at a single slow ramp. Multiply by something like
// 7.0 so several cells land inside the shape.
float vnoise(vec2 p) {
    vec2 i = floor(p), f = fract(p);
    f = f * f * (3.0 - 2.0 * f);              // smooth, so no grid
    return mix(mix(hash21(i),                hash21(i + vec2(1.0, 0.0)), f.x),
               mix(hash21(i + vec2(0.0, 1.0)), hash21(i + vec2(1.0, 1.0)), f.x),
               f.y);
}

// Roughly 0..1, averaging a shade under half. Layering two of these and
// pushing the result is usually what turns noise into something with
// structure rather than grain.
//
// The rotation matrix is written out rather than using rot(), which is
// declared further down: GLSL has no forward declarations, and this block
// sits above it.
float fbm(vec2 p) {
    float sum = 0.0, amp = 0.5;
    for (int i = 0; i < 4; i++) {
        sum += amp * vnoise(p);
        // Rotate as well as scale, so the octaves don't stack into a grid.
        p = mat2(0.8, -0.6, 0.6, 0.8) * p * 2.03 + vec2(1.7, 9.2);
        amp *= 0.5;
    }
    return sum;
}
float bandAt(float x) { return uBands[int(clamp(x, 0.0, 0.999) * 96.0)]; }
mat2 rot(float a) { float c = cos(a), s = sin(a); return mat2(c, -s, s, c); }

float sdCircle(vec2 p, float r) { return length(p) - r; }
float sdEllipse(vec2 p, vec2 r) {
    float k0 = length(p / r);
    float k1 = length(p / (r * r));
    return k0 * (k0 - 1.0) / max(k1, 1e-5);
}
float sdBox(vec2 p, vec2 b) {
    vec2 d = abs(p) - b;
    return length(max(d, 0.0)) + min(max(d.x, d.y), 0.0);
}
float sdRoundBox(vec2 p, vec2 b, float r) { return sdBox(p, b - r) - r; }
float sdSeg(vec2 p, vec2 a, vec2 b) {
    vec2 pa = p - a, ba = b - a;
    float h = clamp(dot(pa, ba) / dot(ba, ba), 0.0, 1.0);
    return length(pa - ba * h);
}
float fill(float d, float w) { return 1.0 - smoothstep(-w, w, d); }

// Dance timing: one move per beat at the detected tempo (up on one beat,
// down on the next), mostly in unison. Size follows the music; at rest
// (silence) groove() is 0 so dancers settle into their rest pose.
float dance(float phase) { return 0.5 + 0.5 * sin(uBeats * PI + phase * 0.35); }
float groove() { return (0.25 + uBass * 0.9 + uBeatStrength * 0.6) * uActive; }
