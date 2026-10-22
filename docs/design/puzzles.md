# Cursory puzzle ideas

Cursory is a co-op physics puzzle game about tethering to objects and pulling them through places. Each player's
cursor is a light physical body pulled toward their pointer by a capped spring. Its tether is a real rope with a
maximum length, so a load heavier than one player's pull drags the cursor toward it. A rope wraps around corners, so
pulling around a corner turns the object, and two players pulling against each other can rotate things one player
can't.

These are 100 puzzle ideas built on that model. Each names the Unity 2D physics pieces it needs. Players means how
many are needed (1+ = solo-able, 2+ = needs two, and so on).

## The rules every puzzle leans on

- **Pull strength.** A player can move a mass up to their pull limit alone; above it, they need help.
- **Leverage.** A rope attached off-centre turns the body; one attached at a corner turns it hardest.
- **Wrap.** A rope that bends around a corner pulls on that corner, not on the cursor's line.
- **Counter-force.** Two ropes on opposite ends pulling in opposite directions make a torque couple, rotating the
  body in place.
- **Rope length.** A rope can't stretch past its maximum, so where a player stands matters.

## A. Corridors and rotation (the "sofa" problems)

1. **The hallway turn.** A long horizontal bar must enter a vertical corridor. One player can drag it but can't turn
   it in the tight bend; two players on opposite ends rotate it. *Players 2+. Rigidbody2D, BoxCollider2D.*
2. **The L-shape.** An L-shaped block must go through a door narrower than its arms. It fits only if turned at the
   exact moment its elbow passes the frame. *Players 2+. CompositeCollider2D.*
3. **The piano stairwell.** A heavy rectangle goes down a zig-zag stairwell, turning 90° at each landing.
   *Players 2+. Rigidbody2D, PhysicsMaterial2D (high friction on landings).*
4. **The ladder through the window.** A thin, long bar must pass diagonally through a square window: line it up
   with the diagonal and slide. *Players 1+. Precision.*
5. **The T through a slot.** A T-shape passes through a slot only stem-first, so it must be flipped 180° in a room
   barely big enough. *Players 2+.*
6. **Pivot on a peg.** A fixed peg in the corridor lets a single player wrap the rope around it and use it as a
   pivot to turn a bar alone. *Players 1+. Rope wrap, static CircleCollider2D.*
7. **The S-bend.** Two turns in opposite directions back to back; the bar must be turned, slid, and turned back.
   *Players 2+.*
8. **The revolving door.** A cross-shaped hinged door turns only if pushed by the object being carried, so the load
   must enter the door at the right angle. *Players 1+. HingeJoint2D (motor off).*
9. **The crowbar.** One bar is stuck under a ledge; use a second bar as a lever to lift it out.
   *Players 2+. Contact friction, torque.*
10. **The spinning plate.** A disc in a round chamber must be spun until its notch lines up with the exit.
    *Players 1+. CircleCollider2D, angular damping.*

## B. Mass and cooperation

11. **Too heavy for one.** A block of mass 2.0 that one player can't budge; two can. (Level 2 already.)
    *Players 2+.*
12. **Three-player crate.** Mass 3.5: three ropes, three players. *Players 3+.*
13. **Unequal partners.** One player's rope is attached near the centre and one at the end, so the end player turns
    it while the centre player translates it. *Players 2+.*
14. **Tug of war.** Two blocks joined by a chain, each needing to reach goals on opposite sides; pull too hard and
    you drag the other team's block off its goal. *Players 2+. DistanceJoint2D chain.*
15. **The anchor.** A player tethers to a heavy static-ish block and becomes an anchor point the others wrap their
    ropes around. *Players 3+.*
16. **The relay.** A block too heavy to drag the whole way alone. Players hand it off at checkpoints, re-tethering
    while another holds it. *Players 2+.*
17. **Balanced lift.** A long plank with a fragile box on top must stay level, so both ends are pulled at the same
    speed. *Players 2+. FixedJoint2D (breakForce) for the fragile box.*
18. **Momentum push.** A lighter block, flung at speed, knocks a heavier one into place. *Players 1+.
    Rigidbody2D velocity, bounciness.*
19. **Counterweight.** Hang a weight off a pulley so a door rises; the heavier the weight, the higher it rises.
    *Players 1+. SliderJoint2D, DistanceJoint2D over a fixed wheel.*
20. **The convoy.** Five small crates linked by short chains; pull the lead and the train snakes through the maze,
    catching on corners unless guided. *Players 1+. HingeJoint2D chain.*

## C. Rope wrapping and corners

21. **Corner spin.** Tether to a block's corner, walk around a pillar, and pull: the block spins toward you.
    *Players 1+. Rope wrap, torque.*
22. **The capstan.** Wrap the rope around a fixed wheel several times so friction lets one player hold a heavy load.
    *Players 1+. Rope wrap around CircleCollider2D.*
23. **Pulley block.** A rope over a ceiling pulley pulls a block straight up into a shelf. *Players 1+.*
24. **Double pulley.** Two pulleys halve the force needed, so one player can lift a mass-2 block slowly.
    *Players 1+. Mechanical advantage.*
25. **Around the island.** The goal is on the far side of a central pillar; the rope must wrap around it to pull the
    block there. *Players 1+.*
26. **Snag hazard.** Spikes on a wall cut a rope that wraps over them; route around them. *Players 1+. Trigger
    collider cuts the joint.*
27. **Unwind.** A block is wrapped twice around a post; unwind it in the right direction before it will come free.
    *Players 1+.*
28. **Rope bridge.** Two players' ropes cross at a pillar, so each pulls the other's block. *Players 2+.*
29. **The sling.** Spin a small block on a short rope and let go to fling it over a wall. *Players 1+. Angular
    velocity, release timing.*
30. **The lasso.** Wrap the rope around a round object to drag it, since it has no corner to grab well.
    *Players 1+. CircleCollider2D, friction.*

## D. Chains, springs and joints

31. **Spring door.** A door on a spring closes again unless held; one player holds while the other pulls the block
    through. *Players 2+. SpringJoint2D.*
32. **Bouncy floor.** A springy floor launches blocks dropped on it; aim the bounce at a ledge. *Players 1+.
    SpringJoint2D or PhysicsMaterial2D bounciness.*
33. **Hinged bridge.** A drawbridge lowers when weight is pulled onto its far end. *Players 1+. HingeJoint2D.*
34. **Chain of keys.** A chain must be laid across three hooks in order. *Players 1+. HingeJoint2D chain, triggers.*
35. **Pendulum gate.** A swinging pendulum blocks the corridor; time the pull to pass between swings. *Players 1+.
    HingeJoint2D with motor.*
36. **The scissor lift.** Pull a lever to extend a scissor platform under a block, lifting it to the next floor.
    *Players 1+. Linked HingeJoint2Ds.*
37. **Wheel and axle.** A cart on wheels moves easily but only along its track; load the block onto it.
    *Players 1+. WheelJoint2D.*
38. **Stretch limit.** A block connected by a spring to the wall: pull to the goal, but the spring yanks it back the
    moment you let go, so a second player pins it. *Players 2+. SpringJoint2D.*
39. **Break the weld.** A block is welded to the floor; pull hard enough together and the weld snaps.
    *Players 2+. FixedJoint2D breakForce.*
40. **Slider rails.** A block moves only along a rail; pull it to the end where it drops off.
    *Players 1+. SliderJoint2D with limits.*

## E. Environment and surfaces

41. **Ice rink.** Frictionless floor: blocks keep sliding, so they must be caught. *Players 1+. PhysicsMaterial2D
    friction 0.*
42. **Sticky zone.** High-friction floor where blocks need extra force. *Players 2+.*
43. **Conveyor belts.** Belts carry blocks the wrong way unless pulled against them. *Players 1+. SurfaceEffector2D.*
44. **Wind tunnel.** A steady wind pushes light blocks but not heavy ones. *Players 1+. AreaEffector2D.*
45. **Magnet.** A magnet pulls metal blocks toward it; pull against it, or use it. *Players 1+. PointEffector2D.*
46. **Water pool.** Light blocks float, heavy ones sink; float a block across to reach a goal. *Players 1+.
    BuoyancyEffector2D.*
47. **One-way floor.** A platform blocks movement one way only. *Players 1+. PlatformEffector2D.*
48. **Moving wall.** A wall slides back and forth; time the pass. *Players 1+. Kinematic Rigidbody2D.*
49. **Crumbling floor.** Floor tiles break under heavy blocks, so take the long way. *Players 1+. Joints with
    breakForce.*
50. **Low gravity zone.** Inside, blocks float and drift. *Players 1+. AreaEffector2D or per-body gravity scale.*

## F. Sorting, goals and precision

51. **Colour sort.** Coloured blocks go to matching pads. *Players 1+.*
52. **Exact fit.** A block must sit in a frame only slightly bigger than itself. *Players 1+.*
53. **Tangram.** Seven shapes combine into one outline on the floor. *Players 1+. Multiple shape goals.*
54. **Stack it.** Stack three blocks into a tower that must stand for five seconds. *Players 2+. Stacking stability.*
55. **Balance scale.** Put blocks on a seesaw until it levels. *Players 1+. HingeJoint2D seesaw.*
56. **Keep it upright.** A tall block that tips over easily must be moved without falling.
    *Players 2+. Centre of mass.*
57. **Weighted switch.** A pressure plate opens a door only while a heavy enough block sits on it. *Players 1+.
    Trigger + mass check.*
58. **Two-plate lock.** Two pressure plates far apart must both be held at once. *Players 2+.*
59. **Order matters.** Blocks must reach their goals in a given order or the earlier ones get pushed off.
    *Players 1+.*
60. **Fragile cargo.** A glass block shatters if it hits anything too hard. *Players 1+. Collision impulse
    threshold.*

## G. Circuits (Cursory's battery, bulb and wires)

61. **Close the loop.** Drag wire ends onto terminals to light the bulb. (Exists.) *Players 1+.*
62. **Short wires.** The wires are too short to reach; move the battery closer first. *Players 1+.*
63. **Two circuits.** Two bulbs, one battery: wire them in series, then in parallel. *Players 1+.*
64. **Powered door.** A lit bulb opens a door the block must go through. *Players 1+. Circuit drives a joint motor.*
65. **Live wire.** A rope touching a live wire drops its block. *Players 1+. Trigger.*
66. **Moving terminal.** A terminal sits on a sliding block that must be held in place while the wire is connected.
    *Players 2+.*
67. **Resistor puzzle.** Swap resistors to dim a bulb to the right brightness. *Players 1+.*
68. **Remote switch.** A switch across the map opens a gate, so one player stays while the other carries.
    *Players 2+.*
69. **Wire as a rope.** Use a wire to tether and drag the battery itself. *Players 1+.*
70. **Timed circuit.** The bulb stays lit for ten seconds; get the block through before the door closes.
    *Players 1+.*

## H. Timing and coordination

71. **Synchronised pull.** Two blocks on two tracks must arrive at the same moment. *Players 2+.*
72. **The crusher.** A slow press comes down; get the block through before it lands. *Players 1+. Kinematic body.*
73. **Rhythm gates.** Gates open and close on a beat; whistle to signal the timing. *Players 2+.*
74. **Hold the door.** One player holds a heavy door open with their rope while others pass. *Players 2+.*
75. **Rotating room.** The whole room slowly rotates, so "down" changes. *Players 1+. Rotating gravity.*
76. **Trade places.** Two blocks in a narrow corridor must swap ends. *Players 2+.*
77. **Pass the rope.** Only one player's rope fits through a gap; they carry the block alone partway, then the team
    re-tethers. *Players 2+.*
78. **Escort.** A slow rolling ball must be kept on a narrow path by players guiding it from both sides.
    *Players 2+.*
79. **Leapfrog.** Two blocks used as stepping stones for a third across a gap. *Players 2+.*
80. **Race against the tide.** Rising water floods the level; get the heavy block out before it floats away.
    *Players 2+. BuoyancyEffector2D with a moving surface.*

## I. Compound shapes and articulated objects

81. **The snake.** A five-segment chain of blocks that must go through a twisting tube. *Players 2+. HingeJoint2D
    segments.*
82. **The folding ruler.** A two-segment hinged bar folds to fit through a short door, then unfolds.
    *Players 2+. HingeJoint2D with limits.*
83. **The wheelbarrow.** A block on one wheel easy to push but hard to steer. *Players 1+. WheelJoint2D.*
84. **The boat.** A floating hull carries a heavy block across water. *Players 1+. Buoyancy.*
85. **The see-through key.** A key-shaped compound block must turn in a lock to open it. *Players 1+.
    CompositeCollider2D, trigger angle check.*
86. **The ring and post.** Lift a ring over a post. *Players 1+. Edge colliders.*
87. **The jigsaw join.** Two pieces snap together only when aligned, then move as one. *Players 2+. FixedJoint2D
    created at runtime.*
88. **The tangled pair.** Two shapes interlocked; separate them by the one rotation that frees them. *Players 2+.*
89. **The gear train.** Turn one gear, and a chain of gears turns a door. *Players 1+. HingeJoint2D + gear
    contacts.*
90. **The cart and trailer.** A cart pulls a trailer through tight turns that the trailer cuts. *Players 1+.*

## J. Big set pieces

91. **The grand piano.** Many of the above in one level: a heavy, odd-shaped object through doors, stairs and a
    lift. *Players 3+.*
92. **The bridge build.** Carry planks into place across a chasm, then carry the cargo over them.
    *Players 2+. Stacking, friction.*
93. **The demolition.** A tower of blocks must be pulled down so one block lands on a goal. *Players 2+.*
94. **The heist.** Get a fragile item past moving lasers and pressure plates without touching anything.
    *Players 2+.*
95. **The ship's anchor.** Raise a huge anchor with all players on capstans. *Players 4+.*
96. **The avalanche.** A chute of rolling boulders must be dodged while dragging cargo uphill. *Players 2+.*
97. **The draw bridge race.** Two teams, two bridges: lower your bridge by pulling your counterweight first.
    *Players 4+, competitive.*
98. **The rescue.** A block stuck in a pit can only be lifted by ropes over two pulleys at once. *Players 2+.*
99. **The machine.** Lever opens gate, gate releases ball, ball hits switch, switch lights bulb (a Rube Goldberg
    chain the players set up). *Players 2+.*
100. **The finale.** Every player's rope on one giant shape, which only turns through the final door if everyone
     pulls in the right direction at once. *Players 4+.*

## Building them

Most of these need nothing beyond what Unity's 2D physics already offers: `Rigidbody2D`, the 2D colliders,
`PhysicsMaterial2D`, and the joints (`DistanceJoint2D`, `HingeJoint2D`, `SpringJoint2D`, `SliderJoint2D`,
`WheelJoint2D`, `FixedJoint2D`, `TargetJoint2D`) and effectors (`AreaEffector2D`, `PointEffector2D`,
`SurfaceEffector2D`, `BuoyancyEffector2D`, `PlatformEffector2D`).

New level elements Cursory doesn't have yet:

- pegs and pulleys the rope wraps around;
- springs and hinged doors;
- surface zones (ice, sticky, conveyor, wind, water);
- pressure plates and timed gates;
- joints that break under force.

The physical cursor and real rope (UC-T33) come first; most of the puzzles depend on them.
