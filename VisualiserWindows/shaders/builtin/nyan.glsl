const float PIX = 1.0 / 72.0;   // chunky pixel grid, in screen-height units

void main() {
    vec2 p = screenP();
    vec2 q = (floor(p / PIX) + 0.5) * PIX;     // pixelate everything
    float step8 = floor(uBeats * 2.0);         // sprite steps twice per beat

    vec3 col = vec3(0.04, 0.2, 0.4);

    // stars rushing left, faster when louder
    vec2 sg = vec2(q.x + uScroll, q.y);
    vec2 sid = floor(sg / 0.16);
    vec2 sl = sg - (sid + 0.5) * 0.16;
    float sh = hash21(sid);
    if (sh > 0.55) {
        vec2 off = (vec2(hash21(sid + 3.0), hash21(sid + 9.0)) - 0.5) * 0.08;
        vec2 a = abs(sl - off);
        float tw = floor(mod(step8 + sh * 7.0, 4.0));
        float arm = PIX * (1.0 + tw);
        float star = (a.x < PIX * 0.6 && a.y < arm) || (a.y < PIX * 0.6 && a.x < arm) ? 1.0 : 0.0;
        col = mix(col, vec3(1.0), star);
    }

    float catX = 0.12;
    float bob = (mod(step8, 2.0) < 1.0 ? 0.0 : PIX) + floor(uBeatStrength * 3.0) * PIX;
    // The cat flies at the height you point at, in whole pixels so the
    // picture stays pixel art.
    float cy = bob + floor(pointerAt().y * 0.25 / PIX) * PIX * uMouseHere;

    // rainbow trail: six stripes, stepping up and down in 8-pixel segments
    float stripeH = PIX * (3.0 + floor(uBass * 3.0));
    // full-length trail while music plays; it grows out from the cat as
    // the music starts and shrinks back into it in silence
    float trail = uActive * 3.0;
    if (q.x < catX - 0.1 && q.x > catX - 0.1 - trail) {
        float seg = floor((catX - q.x) / (PIX * 8.0));
        float wave = mod(seg + step8, 2.0) < 1.0 ? 0.0 : PIX;
        float rel = (q.y - cy - wave + stripeH * 3.0) / stripeH;
        if (rel >= 0.0 && rel < 6.0) {
            int k = int(rel);
            vec3 c = k == 5 ? vec3(1.0, 0.0, 0.0) : k == 4 ? vec3(1.0, 0.6, 0.0) :
                     k == 3 ? vec3(1.0, 1.0, 0.0) : k == 2 ? vec3(0.2, 1.0, 0.0) :
                     k == 1 ? vec3(0.0, 0.6, 1.0) : vec3(0.4, 0.2, 1.0);
            col = mix(c, vec3(1.0), 0.25 * uBeatStrength);
        }
    }

    vec2 c = q - vec2(catX, cy);
    float legStep = mod(step8, 2.0) * PIX;
    // tail and legs (grey, black outline)
    float tail = sdBox(c - vec2(-0.13, 0.01 + legStep), vec2(PIX * 4.0, PIX * 1.0));
    float legs = min(min(sdBox(c - vec2(-0.08 + legStep, -0.09), vec2(PIX * 1.5)),
                         sdBox(c - vec2(-0.03 + legStep, -0.09), vec2(PIX * 1.5))),
                     min(sdBox(c - vec2(0.04 + legStep, -0.09), vec2(PIX * 1.5)),
                         sdBox(c - vec2(0.09 + legStep, -0.09), vec2(PIX * 1.5))));
    float limbs = min(tail, legs);
    if (limbs < PIX) col = vec3(0.0);
    if (limbs < 0.0) col = vec3(0.6);

    // pastry body: crust, frosting, sprinkles
    float crust = sdRoundBox(c, vec2(0.11, 0.085), PIX * 3.0);
    if (crust < PIX) col = vec3(0.0);
    if (crust < 0.0) col = vec3(1.0, 0.8, 0.6);
    float frost = sdRoundBox(c, vec2(0.09, 0.065), PIX * 3.0);
    if (frost < 0.0) {
        col = vec3(1.0, 0.6, 0.85);
        vec2 spid = floor(c / (PIX * 4.0));
        if (hash21(spid) > 0.8 && mod(floor(c.x / PIX) + floor(c.y / PIX), 2.0) < 1.0)
            col = vec3(1.0, 0.2, 0.6);
    }

    // head with ears, eyes, cheeks, mouth
    vec2 h = c - vec2(0.1, -0.02);
    float head = sdRoundBox(h, vec2(0.065, 0.05), PIX * 2.0);
    float ears = min(sdBox(h - vec2(-0.045, 0.055), vec2(PIX * 1.5, PIX * 2.0)),
                     sdBox(h - vec2(0.045, 0.055), vec2(PIX * 1.5, PIX * 2.0)));
    float cat = min(head, ears);
    if (cat < PIX) col = vec3(0.0);
    if (cat < 0.0) col = vec3(0.6);
    if (sdBox(vec2(abs(h.x), h.y) - vec2(0.028, 0.012), vec2(PIX * 1.0)) < 0.0) col = vec3(0.0);
    if (sdBox(vec2(abs(h.x), h.y) - vec2(0.045, -0.016), vec2(PIX * 1.0)) < 0.0) col = vec3(1.0, 0.6, 0.65);
    if (sdBox(h - vec2(0.0, -0.022), vec2(PIX * 2.0, PIX * 0.5)) < 0.0) col = vec3(0.0);

    fragColor = vec4(col, 1.0);
}