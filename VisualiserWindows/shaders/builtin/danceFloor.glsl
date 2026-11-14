uniform vec3 uPalette[3];

void main() {
    // --- the camera, on the beat --------------------------------------
    // Every axis runs off the beat clock rather than free-running, so the
    // motion lands with the music instead of drifting against it: a sharp
    // dip on each kick that settles, a roll on the two-beat, a strafe and a
    // slow sweep left and right underneath it. All of it stops dead in
    // silence, because the clocks that feed uBeats do.
    float phase = fract(uBeats);
    float kick = exp(-phase * 6.0) * uBeatStrength * uActive;
    float roll = (sin(uBeats * 0.5) * 0.05 + kick * 0.022) * uActive;
    vec2 p = rot(roll) * screenP();
    float pan = sin(uBeats * 0.25) * 1.15 * (0.3 + 0.7 * uBass) * uActive;   // strafe
    float travel = cos(uBeats * 0.5) * 0.55 * uActive;                      // up and down the floor
    // The floor slides with the pointer, so moving the mouse walks you
    // across the dance floor.
    float look = sin(uBeats * 0.125) * 0.30 * uActive
               + pointerAt().x * 0.10 * uMouseHere;                        // which way we face
    float horizon = 0.10 - kick * 0.035 - uBass * 0.02 * uActive;            // dips on the kick

    vec3 col = vec3(0.015, 0.012, 0.03);         // the room is nearly dark

    // Above the horizon: a wash of the palette's shadow colour, and a bright
    // line where the floor meets it.
    col += uPalette[0] * 0.30 * smoothstep(horizon, 0.5, p.y);
    col += uPalette[1] * 0.10 * exp(-abs(p.y - horizon) * 26.0) * uActive;

    if (p.y < horizon) {
        // Perspective: solve the distance to the floor point, which is the
        // z of a camera ray hitting the horizon.
        float depth = 0.34 / (horizon - p.y) - travel;
        float fade = exp(-max(0.0, depth) * 0.30);         // and fade out into the room
        if (fade > 0.02) {
            float x = (p.x - look) * depth + pan;
            vec2 id = floor(vec2(x, depth));
            vec2 f = fract(vec2(x, depth));

            // The whole floor breathes with the bass; a wave travels out
            // from the middle of the room on each beat.
            float lift = (0.05 + 0.13 * uBass) * uActive;
            float wave = exp(-mod(uBeats * 0.5 - (id.x + id.y) * 0.5, 8.0) * 2.2) * uBeatStrength;
            float pop = lift + wave * 0.22;

            // Which face of the cube this tile is showing. The whole floor
            // re-solves over time, faster when the mids are busy. The hash
            // is offset per row as well as per tile so neighbouring tiles
            // don't all land on the same face.
            float pick = hash21(id) * 0.7 + hash21(id.yx) * 0.3;
            float idx = mod(floor(pick * 6.0 + uBeats * (0.2 + uMid * 0.8)), 6.0);
            vec3 face = uPalette[1];
            if (idx < 1.0)      face = vec3(0.85);                    // white
            else if (idx < 2.0) face = uPalette[0] * 3.4;             // a near-black face
            else if (idx < 3.0) face = vec3(0.04);
            else if (idx < 4.0) face = uPalette[2];                   // the mid tone
            else if (idx < 5.0) face = uPalette[1] * 0.42;            // deep magenta
            else                face = mix(uPalette[1], vec3(0.75, 0.95, 1.0), 0.55); // cold chrome

            // A little variation even within one face, so a run of the same
            // colour still has depth.
            face *= 0.82 + 0.36 * hash11(floor(pick * 97.0));

            // A moving sheen, so the tiles read as polished rather than flat.
            float sheen = pow(max(0.0, 1.0 - abs((f.x + f.y * 0.6) - 0.8)), 6.0);
            face += vec3(0.9) * sheen * (0.12 + 0.22 * uTreble * uActive);

            vec2 q = f - 0.5;
            q.y -= pop * 0.55;                    // the tile rises as it pops
            float tile = 0.44 - pop * 0.16;
            float d = sdRoundBox(q, vec2(tile), 0.05);
            float inside = fill(d, aaw(0.4));

            // The floor between the tiles, brightening toward the viewer.
            vec3 base = uPalette[0] * (0.35 + 0.5 * exp(-max(0.0, depth) * 0.16));
            col = mix(col, base, fade);
            col = mix(col, face * (0.30 + 0.85 * uBeatStrength * uActive), inside * fade);

            // Neon edges between the tiles, glittering on the hats.
            float edge = min(min(f.x, 1.0 - f.x), min(f.y, 1.0 - f.y));
            float line = 1.0 - smoothstep(0.0, 0.05 + 0.05 * uTreble * uActive, edge);
            col += uPalette[1] * line * (0.20 + 0.45 * uTreble * uActive) * fade;

            // A hint of the room bouncing off the floor on each kick.
            col += uPalette[1] * uBeatStrength * 0.05 * fade;
        }
    }
    fragColor = vec4(min(col, vec3(1.0)), 1.0);
}