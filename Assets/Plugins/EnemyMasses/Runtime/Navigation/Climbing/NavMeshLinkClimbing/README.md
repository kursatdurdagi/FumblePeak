# NavMeshLink-Based Climbing System

A more reliable climbing system that uses Unity's built-in NavMeshLinks for pathfinding integration.

## Benefits Over Manual Climbing

| Feature | Manual Climbing | NavMeshLink Climbing |
|---------|----------------|---------------------|
| Pathfinding | Not integrated | Native integration |
| Agent State | Disable/enable NavMeshAgent | Agent stays on NavMesh |
| Direction | Manual zone detection | Implicit in link traversal |
| Reliability | Complex state management | Simpler, fewer bugs |
| 1000+ Agents | Possible but complex | Works naturally |

## Components

### NavMeshLinkClimbable
Attach to a wall/climbable surface. Creates multiple NavMeshLinks across the width.

**Settings:**
- `Width/Height`: Dimensions of the climbing surface
- `Link Count`: Number of parallel climbing lanes (more = more simultaneous climbers)
- `Link Width`: Width of each link (affects agent spacing)
- `Cost Modifier`: Higher = agents prefer walking around instead of climbing
- `Traversal Time`: How long it takes to climb

### NavMeshLinkTraversalHandler
Central controller that handles custom traversal animation. Add ONE to your scene.

**Settings:**
- `Speed Multiplier`: Adjust climbing speed globally
- `Auto Complete Traversal`: Let the handler manage link completion

### NavMeshLinkCrowdAnimationHandler (Optional)
Integrates with GPU Instancer Pro Crowd Animations for climb animations.

## Setup Guide

1. **Add NavMeshLinkClimbable** to your wall:
   - Position at the base of the wall
   - Set Width/Height to match the wall
   - Adjust Link Count (5-10 for small walls, 20-50 for wide walls)

2. **Add NavMeshLinkTraversalHandler** to a manager object in your scene

3. **Bake NavMesh** - The links will automatically connect

4. **Test** - Agents will now automatically pathfind through climb links!

## Performance for Large Swarms

For 1000+ zombies climbing:

```
Link Count: 30-50 (distribute agents across width)
Link Width: 1.0-2.0 (allows overlapping)
Cost Modifier: 2-5 (slightly prefer ground paths)
```

## Comparison with Old System

The old manual climbing system:
- Disables NavMeshAgent during climb
- Manually tracks position along waypoints
- Requires complex entry/exit zone detection
- Can leave agents in broken state if they die

The NavMeshLink system:
- Agent remains on NavMesh (just traversing a link)
- Unity handles the off-mesh link state
- Direction is implicit (link start → end)
- Clean recovery if agent dies (just stops traversing)

## Limitations

- Requires NavMesh Surface with proper baking
- Links must connect valid NavMesh areas
- Animation sync requires GPUInstancer Pro integration
