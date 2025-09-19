# Phaseline

Phaseline is a retro-inspired hoverbike arena game developed for CISC 486 (Game Development).  
It features PSX-style graphics, fast-paced movement, glowing trails, portals, and power-ups.  
Players must outmaneuver rivals to be the last bike standing.


## Gameplay Overview
- Competitive multiplayer arena battles (2–4 players online).  
- Hoverbikes leave glowing trails that eliminate on contact.  
- Sharp 90° turns for classic arcade handling.  
- Portals teleport bikes across the arena while conserving momentum.  
- Power-ups provide temporary advantages (boost, trail immunity, laser attack).  
- Anti-gravity mechanics allow bikes to stick to walls/ceilings until a boost or jump breaks adhesion.  
- NPC AI enemies (FSM + decision-making) provide challenge in single-player or hybrid matches.  


## AI Design
- **Finite State Machine (FSM):** States include `Patrol → Chase → Predictive Cut off → Evade`.  
- **Decision-Making / Pathfinding:** NPCs decide whether to chase, cut off, use portals, or grab power-ups.  


## Scripted Events
- Portals activating/deactivating at set intervals.  
- Randomly timed power-up spawns.  
- Arena shrinks after every quarter of round, forcing players inward.
- Retro glitch/destabilization effects to intensify endgame phases.  


## Environments and Assets
- PSX-inspired low-poly models and pixelated textures for hoverbikes and arenas (created  in Blender).  
- Up to 4 different arenas/maps to select from.
- [Sci-fi effects](https://assetstore.unity.com/packages/vfx/particles/sci-fi-arsenal-60519) for trails, portals and power-ups.  
- [Sci-Fi weapons](https://assetstore.unity.com/packages/audio/sound-fx/weapons/sci-fi-weapons-pack-1-218039) sound effects.
- Synthwave soundtracks with escalating intensity. 


## Team Plan
- **Shahbaaz Siddiqui** (solo developer):  
  - Core gameplay (bike controller, trails, portals, anti-gravity).  
  - AI design (FSM, decision-making).  
  - Power-ups and scripted events.  
  - Networking integration (Unity Netcode for GameObjects).  
  - 3D models.
  - Documentation.  


## Additional Goals
- Laser weapon as an additional attack option.  
- Local split-screen multiplayer.  
- Arena variants (loops, vertical surfaces, portal-heavy maps). 
