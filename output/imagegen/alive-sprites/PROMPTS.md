# LuKnight living sprite pack

Generated with the built-in image_gen tool. Identity reference: Assets/Characters/LuKnight/Reference/LuKnightReference.png. Rig design referenced the new right-profile Walk atlas. Final runtime frames and parts are imported into Assets/Characters/LuKnight by tools/Import-SpriteSheets.ps1.

## common

Game sprite atlas, exactly 4 columns x 2 rows (8 equal square cells), 2048x1024. One whole rabbit per cell, SAME rabbit identity as supplied reference: soft plush white rabbit, cyan-lined floppy ears, huge glossy cyan-black star eyes, pink nose and blush, cyan chest star. Professional character animation drawings. All cells same orthographic camera, anatomical scale, head size, fixed body center and feet baseline, ample padding. Actual transparent background, no checkerboard, no floor/shadows, no text.

## walk

WALK CYCLE facing RIGHT in strict SIDE PROFILE (90-degree side view): nose/muzzle points clearly to the RIGHT, one large NEAR EYE visible; far eye mostly hidden. Body and head both turned right; absolutely NEVER face the viewer. TWO distinct legs, near leg white and far leg slightly shaded lavender gray, BOTH visible and moving in OPPOSITE phases. Arms swing opposite legs. Frame order: 1 near leg FORWARD far leg BACK contact; 2 near leg planting far leg lifting; 3 near leg BACK beneath hip far leg FORWARD passing; 4 near leg fully BACK far leg FORWARD contact; 5 near leg BACK far leg FORWARD contact (opposite to frame1); 6 near leg lifting far leg planting; 7 near leg FORWARD passing far leg BACK; 8 near leg FORWARD far leg BACK returns toward frame1. Feet are clearly separated horizontally at contact poses. NO stationary foot throughout cycle, no hopping. Ears trail LEFT behind head. Head does not bob or change size. Show a believable complete biped walking cycle with alternating foot contacts.

## climbing

CLIMBING CYCLE in SIDE PROFILE facing RIGHT towards an invisible wall on RIGHT; nose points RIGHT, gaze up/right, NOT at viewer. BOTH hands and BOTH feet visible. Alternating diagonals: near hand high with far knee lifted, pull torso slightly up, swap to far hand high with near knee lifted, pull up, repeat. Eight chronological frames. Hands reach towards RIGHT. No actual wall or ledge. Head and ear scale stays identical, body stays approximately centered while limbs cycle. Ears respond subtly.

## idle

IDLE CHARACTER ACTING CYCLE, front three-quarter as reference. Exactly eight chronological subtle poses: 1 relaxed gentle smile eyes open; 2 same pose inhale slightly; 3 half-closed eyelids; 4 both eyes fully closed natural blink; 5 eyes reopen; 6 left ear tip gently flexes; 7 ear relaxes and tiny head tilt; 8 return to exact first pose. BOTH feet remain planted in IDENTICAL locations, same head size, body placement, no arm waving, no jumping. Tiny natural changes only. This is a breathing/blinking/ear-twitch idle loop.

## common

Game sprite atlas, exactly 4 columns x 2 rows (8 equal square cells), 2048x1024. One whole rabbit per cell, SAME identity as supplied reference: soft plush white rabbit, cyan-lined floppy ears, huge glossy cyan-black star eyes, pink nose and blush, cyan chest star. All cells same orthographic camera, anatomical scale, head size, fixed body center and feet baseline, ample padding. Actual transparent background, no checkerboard, no floor/shadows, no text.

## sleep

SLEEP BREATHING LOOP, sitting curled with eyes closed, hands resting on belly, floppy ears drooping close to head. All 8 frames same seated pose and location. Belly and shoulders subtly rise through frames 1-4 on inhale and settle through frames 5-8 on exhale, nose relaxes, ear tips sag then gently recover. Head anatomical size identical to reference, don't enlarge seated rabbit to fill cell. No sudden different pose, no waking up, no stars or floating Z symbols.

## grabbed

GRABBED DANGLING LOOP, front-three-quarter rabbit suspended with both feet off ground, no human or hand drawn. Head position and size FIXED, body hangs below head, ears droop. BOTH legs wriggle/kick gently in alternating opposite phases, knees bending with both feet clearly visible. Arms reach out a little then relax; worried cute face, eyebrows and mouth reacting. Frame1 near knee lifted far leg extended; progress to frame5 opposite legs then return by8. Keep movements modest and body same size.

## falling

FALLING LOOP, front three-quarter falling rabbit arms spread, ears floating upwards, startled face. Same head size and stationary torso center through eight frames. Both legs bicycle gently in opposite phases in the air, palms subtly open and close, ears gently flutter. Frame1 near leg up far leg down; progress smoothly to opposite byframe5 then returnby8. NO tumbling, no spinning, no silhouette-size change, no dramatic pose jumps. No ground drawn.

## hanging

HANGING LOOP, front three-quarter rabbit hanging from invisible ledge above. BOTH hands clearly raised ABOVE head at top left and top right of head, forearms beside cheeks, elbows bend subtly. Paws stay in the SAME anchor position everyframe. Ears flop BETWEEN raised hands rather than exceeding canvas. Body gently swings 2 degrees, both feet dangle and alternate tiny kicks. Cute effort expression, brief blink at frame4, return to start byframe8. Same head size and head position, no ledge drawn.

## Cutout rig

Production 2D CUTOUT ANIMATION RIG sprite parts atlas for the reference rabbit, STRICT RIGHT SIDE PROFILE, nose points RIGHT and one eye visible. Exactly THREE columns by TWO rows, six isolated parts on TRUE TRANSPARENT background (no checkerboard/no floor). Each cell separate isolated asset, not touching adjacent cells. Preserve white plush shading, cyan floppy ears/eye, pink nose, cyan chest star from reference. TOP LEFT: complete HEAD + EARS + TORSO + TAIL connected, but absolutely NO ARMS AND NO LEGS (torso ends in rounded pelvis, no feet). Top middle: one isolated NEAR ARM, white fur upper arm with rounded paw, hanging down vertically in relaxed rest pose, rounded shoulder pivot at TOP. Top right: one isolated FAR ARM, same shape but subtly lavender shaded, hanging down, shoulder at TOP. BOTTOM LEFT: one isolated NEAR LEG with white fur, thigh+shin+rounded rabbit foot/toes pointing RIGHT, rest pose leg vertical, rounded hip pivot at TOP, full leg including rounded foot. Bottom middle: one isolated FAR LEG, identical anatomical shape but subtly lavender shaded, foot points RIGHT, hip pivot TOP. Bottom right: complete assembled rabbit SIDE PROFILE as placement reference, standing with both legs and both arms. Keep clean smooth edges and seamless overlapping rounded joint bases. Soft polished illustration. No labels, no borders, no texture in background. Output 1536x1024.

## Background correction

Change only the background to flat pure green #00FF00; preserve all poses/parts, sizes, layout, colors and positions. No checkerboard, texture, gradients, shadows, or green spill. Used for Sleep, Grabbed and Rig before chroma-key import.
