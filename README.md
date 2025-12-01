# Phaseline

Phaseline is a retro-inspired hoverbike arena game developed for CISC 486 (Game Development).  
It features PSX-style graphics, fast-paced movement, glowing trails, portals, and power-ups.  
Players must outmaneuver rivals to be the last bike standing.

## 🚀 Getting Started

### Prerequisites
* Unity 6+ (Recommended)
* **FishNet: Networking Evolved** (Free package from the Asset Store)

### Setup & Installation
1.  **Clone the Repository**:
    ```bash
    git clone [https://github.com/ShahbaazS/phaseline.git](https://github.com/ShahbaazS/phaseline.git)
    ```
2.  **Open in Unity**: Add the project folder to Unity Hub and open.
3.  **Install FishNet**: If not already present in `Packages`, download and import FishNet from the Unity Asset Store or Package Manager.
4.  **Scene Setup**: Open the main gameplay scene (e.g., `Scenes/Arena_Main`).

### How to Run
1.  **Enter Play Mode** in the Unity Editor.
2.  **Start Networking**:
    * The project uses FishNet's `NetworkManager`.
    * To play alone or with bots: Click **Server** (or Host) in the FishNet HUD.
    * To test multiplayer locally: Build a standalone player for the Client, run it, and click **Client**. Run the Editor as **Server/Host**.
3.  **Spawning**:
    * The `SpawnManager` automatically spawns **Bots** on server start.
    * **Player Bikes** are spawned automatically when a client connects.

---

## 🎮 Gameplay Overview
* **Objective**: Outlast opponents in a shrinking arena. Avoid colliding with walls or the glowing trails left by bikes.
* **Movement**: Anti-gravity hoverbikes with sharp 90° arcade turning and drift mechanics.
* **Trails**: Bikes leave physical "light walls" behind them. Hitting a trail results in immediate elimination.
* **Portals**: Teleport across the arena to escape traps or cut off enemies.

### Power-Ups (New!)
The arena now features collectible power-ups that respawn over time. Both Players and AI can utilize them.

* **⚡ Boost**: Temporarily increases bike speed by 150%. Useful for escaping tight spots or overtaking enemies.
* **🛡️ Shield**: Grants temporary invulnerability (2-4 seconds). Allows you to pass through trails or survive wall impacts without dying.

---

## 🌐 Networking Architecture (FishNet)
This project has migrated from Unity Netcode for GameObjects (NGO) to **FishNet** to leverage robust Client-Side Prediction (CSP).

### 1. Client-Side Prediction (CSP)
* **NetworkBike**: Implements `PredictionRigidbody` to ensure the local player experiences instant, lag-free movement input (`[Replicate]`), while the server maintains authority (`[Reconcile]`).
* **Input Sync**: Inputs (Throttle, Steering, Drift, Jump) are sent to the server every tick. If the server simulation disagrees with the client, the client is "rolled back" and re-simulated to match the server state.

### 2. Bandwidth Optimization
* **Trails**: Trail meshes are **not** sent over the network. Instead, `NetworkTrailMesh` generates visuals locally on every client based on the bike's position.
* **Events**: RPCs (`[ObserversRpc]`) are used strictly for critical state changes like **Teleporting**, **Respawning**, or **Clearing Trails**, keeping bandwidth usage extremely low.

### 3. Spawn Management
* **SpawnManager**: Handles the lifecycle of players and bots. It listens for FishNet connection events to spawn player prefabs and assign ownership dynamically.
* **Spawn Protection**: A `SpawnProtection` component grants 2 seconds of invulnerability and disables trail generation upon respawning to prevent unfair spawn-killing.

---

## 🤖 AI Design
**Finite State Machine (FSM)**

The AI bot controlling enemy hoverbikes uses a **Finite State Machine** (FSM) to balance aggression, survival, and navigation. Each bot continuously analyzes space around it using **spherecasts** (forward, side, and diagonal probes).

### **States**
* **Scout**: Default behavior. Moves forward, scanning for open space.
* **Evade**: Triggered when obstacles are detected on sides. Prioritizes survival.
* **Emergency**: Activated by immediate frontal threats. Executes a hard **Drift Turn** to escape collision.
* **Cutoff**: Intercepts opponents crossing the bot's path to trap them.
* **Pathfinding**: Overrides steering to navigate toward high-value targets (Portals, Power-ups, Other Players/Bots).

### **Decision Making**
The AI now utilizes a weighted scoring system to prioritize goals:
* **Power-Ups**: High priority. Bots perceive Power-ups as "closer" than they actually are (`weight: 0.7`) to encourage aggressive acquisition of Boosts and Shields.
* **Portals**: Standard priority (`weight: 1.0`). Used for traversal when no immediate threats or power-ups are nearby.

---

## 🛠 Environments and Assets
* **Visuals**: PSX-inspired low-poly models and pixelated textures (Blender).
* **Audio**: Synthwave soundtracks and Sci-Fi weapon SFX.
* **VFX**: Sci-fi particle effects for trails, boosts, and portals.

## 👥 Team Plan
* **Shahbaaz Siddiqui** (Solo Developer):
    * Core gameplay & Bike Controller.
    * **Networking integration (FishNet)**.
    * AI Design & FSM.
    * Power-ups & Scripted Events.
    * 3D Modeling & Documentation.

## 🔮 Additional Goals
* Laser weapon as an additional attack option.
* Local split-screen multiplayer.
* Arena variants (loops, vertical surfaces).

## 📹 Gameplay Videos
* **FSM Demo**: https://youtu.be/14jkTXgq2Fs
* **Pathfinding Logic**: https://youtu.be/na4aE98YMPQ
* **Networking Demo**: https://youtu.be/BP-wChUkpvE