# Lu-Knight sprite generation prompts

Tool: built-in imagegen. Identity reference: original LuKnight master image. No CLI/API fallback.

Shared specification: production soft-3D desktop pet sprite sheet; white plush chibi rabbit with cyan inner ears and diamond chest emblem, glossy cyan-black eyes, pink nose/cheeks. Same model, fixed orthographic camera facing right, fixed lighting and scale, full ears and feet, transparent alpha, no text/grid/props. Four columns by four rows, sixteen sequential frames, row-major seamless loop. Clean silhouette and generous cell padding.

- Idle: subtle breathing, inhale frames 1?8 and exhale 9?16, gentle ear follow-through and blink around 10?12; no walking or camera motion.
- Walk: in-place right-facing walk cycle, alternating foot contact/down/passing/up, opposite arm swing, small body bounce and ear lag.
- Sleep: curled asleep on side, head right, paws tucked, ears resting; subtle belly breathing; no standing, open eyes or floating Z text.
- Grabbed: suspended dangling pose, loose legs, surprised cute expression, small pendulum cycle; head/grip fixed, no visible hand/rope/hook.
- Falling: airborne, legs slightly tucked, arms out for balance, ears trailing; gentle flutter and body tilt, not walking.
- Hanging: gripping an invisible ledge on right with paws raised, loose body below, eyes toward grip and subtle sway; no drawn wall/ledge.
- Climbing: alternating hand-over-hand and opposite leg climb in place, side view right, small returning body motion; no visible wall.
- Expressions: preserve standing Idle model and body; row 1 happy/wink/sad/dizzy, row 2 angry/surprised/determined/neutral, repeat rows 3?4. Change face only.

Selected source sheets are named by state in this directory. Sleep and Expressions underwent imagegen background-extraction edits: remove all checkerboard/gray backdrop, preserve each figure and placement, deliver real transparent alpha. Opaque or identity-drifting variants were discarded.

The importer splits foreground components, retains the principal rabbit silhouette, removes disconnected alpha flecks, aligns/resizes onto 510x660 RGBA frames and exports 16 frames/state. It exports seven expression files and uses the neutral expression as the new reference master. The reference overview is assembled from the final runtime assets.
