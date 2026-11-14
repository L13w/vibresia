void hamsterAt(inout vec3 col, vec2 q, float w, float kind, float ph, float hot) {
    float d = dance(ph);
    // The hamster you are pointing at gets going: it dances twice as hard,
    // and it grows a little, the way a hamster does when it thinks it is
    // being watched.
    float g = groove() * (1.0 + hot * 2.2);
    q *= 1.0 - hot * 0.22;
    // four dance styles, picked per tile
    if (kind < 0.25)      q.y -= abs(sin(uBeats * PI + ph * 0.35)) * 0.15 * g;   // bounce
    else if (kind < 0.5)  q = rot(uTime * 3.0 + ph) * q;                        // spin
    else if (kind < 0.75) q.x -= (d - 0.5) * 0.25 * g;                          // shuffle
    else                  q = rot((d - 0.5) * 0.7 * g) * q;                     // wiggle

    vec3 fur = vec3(0.9, 0.6, 0.28), belly = vec3(1.0, 0.95, 0.85), pink = vec3(1.0, 0.65, 0.7);
    // feet and waving arms
    col = mix(col, pink, fill(sdEllipse(vec2(abs(q.x), q.y) - vec2(0.12, -0.3), vec2(0.07, 0.035)), w));
    float armUp = d * 0.18;
    col = mix(col, fur * 0.85, fill(sdSeg(vec2(abs(q.x), q.y), vec2(0.18, -0.05), vec2(0.3, 0.02 + armUp)) - 0.04, w));
    // round body, head blended in
    float body = min(sdCircle(q - vec2(0.0, -0.08), 0.24), sdCircle(q - vec2(0.0, 0.14), 0.19));
    col = mix(col, fur, fill(body, w));
    col = mix(col, belly, fill(sdEllipse(q - vec2(0.0, -0.1), vec2(0.15, 0.16)), w));
    col = mix(col, belly, fill(sdCircle(vec2(abs(q.x), q.y) - vec2(0.1, 0.08), 0.075), w));
    col = mix(col, pink, fill(sdCircle(vec2(abs(q.x), q.y) - vec2(0.13, 0.3), 0.06), w));
    col = mix(col, vec3(0.05), fill(sdCircle(vec2(abs(q.x), q.y) - vec2(0.07, 0.17), 0.028), w));
    col = mix(col, vec3(1.0), fill(sdCircle(vec2(abs(q.x), q.y) - vec2(0.062, 0.18), 0.009), w));
    col = mix(col, pink * 0.9, fill(sdEllipse(q - vec2(0.0, 0.11), vec2(0.025, 0.018)), w));
}

// User-supplied sprites (see SpriteAtlas); uSpriteKinds == 0 means draw our own.
// The precision is stated because GLSL ES (which a browser's WebGL2
// context speaks) has no default precision for a sampler array, and
// without one the shader will not compile there.
uniform highp sampler2DArray uSprites;
uniform int uSpriteKinds;
uniform float uSpriteFirst[16];
uniform float uSpriteFrames[16];
uniform float uSpriteDelay[16];

uniform vec2 uSpriteCell;   // layer size in image pixels
uniform float uSpriteClock; // GIF playback time, scaled to the detected BPM

// Tile the user's GIFs edge to edge at their native pixels, scaled up by a
// whole number so pixel art stays crisp. All tiles animate in sync like the
// original page; the wall bounces and pumps with the music.
void spriteWallpaper(inout vec3 col) {
    float scale = max(1.0, floor(uResolution.y / (uSpriteCell.y * 1.4)));
    vec2 px = (gl_FragCoord.xy - 0.5 * uResolution) / (scale * (1.0 + uBeatStrength * 0.04));
    px.y -= floor(abs(sin(uBeats * PI)) * uBass * uActive * 6.0);   // whole-pixel bounce per beat
    px += 0.5 * uSpriteCell;                                      // a tile sits centered
    vec2 cellId = floor(px / uSpriteCell);
    vec2 st = fract(px / uSpriteCell);
    int kind = int(mod(cellId.x + cellId.y, float(uSpriteKinds)));
    float frame = floor(mod(uSpriteClock / uSpriteDelay[kind], uSpriteFrames[kind]));
    vec4 c = texture(uSprites, vec3(st.x, 1.0 - st.y, uSpriteFirst[kind] + frame));
    col = col * (1.0 - c.a) + c.rgb;     // premultiplied alpha
}

void main() {
    vec2 p = screenP();
    float tile = 0.25;
    vec2 sp = p + vec2(uTime * 0.03, uTime * 0.02);   // slow scroll
    vec2 id = floor(sp / tile);
    vec2 q = (fract(sp / tile) - 0.5) * 1.0;
    float h = hash21(id);
    // The tile under the pointer is the one that lights up and dances.
    vec2 mouseId = floor((pointerAt() + vec2(uTime * 0.03, uTime * 0.02)) / tile);
    float hot = (abs(id.x - mouseId.x) < 0.5 && abs(id.y - mouseId.y) < 0.5) ? 1.0 : 0.0;

    // pastel checkerboard that shifts hue on beats
    float checker = mod(id.x + id.y, 2.0);
    vec3 bg = 0.78 + 0.22 * cos(PI * 2.0 * (h * 0.3 + checker * 0.5 + uTime * 0.05 + vec3(0.0, 0.33, 0.67)));
    bg = mix(bg, vec3(1.0), 0.35 * uBeatStrength * checker);
    // The tile under the pointer is lit, and lit warmly: it is the one
    // you are pointing at, and it should look like it.
    bg = mix(bg, vec3(1.0, 0.95, 0.78), 0.9 * hot * uMouseHere);
    vec3 col = bg;
    if (uSpriteKinds > 0) {
        col = mix(vec3(1.0), vec3(1.0, 0.92, 0.97), uBeatStrength);   // white page, flashes on beats
        spriteWallpaper(col);
    } else {
        hamsterAt(col, q * 1.25, aaw(tile / 1.25), hash21(id + 7.0), h * 6.28, hot);
    }
    // A follow-spot, over the top of whichever hamsters are dancing —
    // ours or the person's own GIFs. The light follows the cursor, and
    // the one it lands on is the one that gets going.
    vec2 toSpot = p - pointerAt();
    col += vec3(1.0, 0.86, 0.55) * exp(-dot(toSpot, toSpot) * 7.0) * uMouseHere * 0.30;
    fragColor = vec4(col, 1.0);
}