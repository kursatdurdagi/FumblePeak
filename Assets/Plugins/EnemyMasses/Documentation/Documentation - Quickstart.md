COMPLETE ONLINE DOCUMENTATION: https://arawn-software-publishing.gitbook.io/enemy-masses/

Discord: https://discord.gg/zjR5WU3TNU
E-Mail: mail@arawn.digital

# Getting Started with Enemy Masses

Welcome to **Enemy Masses**! This guide will help you set up the asset and get your first massive crowd running in minutes.

## Prerequisites

Before you begin, ensure you have the following installed in your project:
*   **Unity 6 LTS** or higher
*   **GPU Instancer Pro** (Required for rendering).
*   **Game Creator 2** (Optional, but recommended for logic).

## 1. Initial Setup

After importing the Enemy Masses package into your project, the first thing you should do is run the **Setup Wizard**. This tool is designed to configure your project settings and help you create your first enemy faction without manual setup.

### Opening the Setup Wizard

To open the wizard manually:
1.  Go to the top menu bar.
2.  Select **Tools > Enemy Masses > Setup Wizard**.

## 2. Using the Setup Wizard

The Setup Wizard will guide you through the following steps:

1.  **Welcome & Overview:** A brief introduction to the core concepts of Enemy Masses.
2.  **Crowd Type:** Choose between different crowd behaviors (e.g., Aggressive, Passive).
3.  **Animator Workflow:** Configure how animations are handled for optimal performance.
4.  **Faction Creation:** Define your first enemy faction (e.g., "Orcs", "Zombies").
5.  **Combat & RTS Setup:** Configure combat styles and RTS-like controls if needed.
6.  **Prefab Assignment:** Assign your enemy prefab models.
7.  **Scene Setup:** Automatically adds necessary managers to your current scene.

## 3. Critical Step: Patching GPU Instancer Pro

**This is a highly recommended step.**

Enemy Masses includes a patching system for **GPU Instancer Pro** to fix known issues and improve compatibility, especially with Unity 6.

### Why Patch?
The patch addresses several critical bugs in the original GPU Instancer Pro package:
*   **Fixes Invisible Agents:** Resolves issues where agents would spawn but remain invisible.
*   **Fixes Index Errors:** Prevents `ArgumentOutOfRangeException` and `IndexOutOfRangeException` during rapid spawn/despawn cycles.
*   **Unity 6 Compatibility:** Fixes UI Toolkit threading errors related to `[HelpURL]` attributes in the Inspector.

### How to Patch
The Setup Wizard will prompt you to apply these patches. We strongly recommend clicking **"Apply Patches"** when asked.

If you need to apply them manually later:
1.  Go to **Tools > Enemy Masses > Debug > Patch GPU Instancer Pro**.
2.  Follow the on-screen prompts.

> **Note:** These patches modify the source files of the GPU Instancer Pro package in your `Packages` folder. If you update GPU Instancer Pro, you may need to re-apply these patches.

## 5. Manual Scene Setup (Advanced)

If you prefer to set up your scene manually or need to integrate Enemy Masses into an existing scene without the wizard, follow these steps.

### Core Components (Required for All Types)

Every scene using Enemy Masses requires two main components:

1.  **EnemyMassesCrowdController**:
    *   Create a new GameObject named `EnemyMassesCrowdController`.
    *   Add the `EnemyMassesCrowdController` component.
    *   **Important**: Enable the `Register Immediately` checkbox.
    *   Assign your `EnemyCrowd` ScriptableObjects to the `Enemy Crowds` list.

2.  **GPUIPrefabManager** (from GPU Instancer Pro):
    *   Create a new GameObject named `GPUIPrefabManager` (or use the one from GPUI menu).
    *   Add the `GPUIPrefabManager` component.
    *   Enable `Find Instances At Initialization` and `Register Immediately`.
    *   **Crucial**: Assign a `GPUIPrefabProfile` (create one if needed) and add your enemy prefabs to the `Prototype Settings` list.
    *   Link this manager to the `Prefab Manager` field on your `EnemyMassesCrowdController`.

### Setup for Combat Crowds (Action/RPG)

For standard 3rd-person or action games where enemies attack the player:

1.  Select your `EnemyMassesCrowdController`.
2.  Assign your Player's Transform to the `Player Target` field.
3.  Ensure your `EnemyCrowd` assets have `Enable Combat System` checked.

### Setup for RTS / Battle Simulation

For Top-Down strategy games or large scale battle simulations:

1.  **Controller Settings**:
    *   On `EnemyMassesCrowdController`, enable `Faction Battle Mode`.
    *   Add the `EnemyMassesRTSController` component to the same GameObject.
    *   Add the `EnemyMassesProjectileSystem` component (handles arrows/magic).

2.  **Camera Setup**:
    *   Select your Main Camera.
    *   Add the `GPUICamera` component (required for rendering).
    *   Add the `RTSCameraController` component for movement controls.

3.  **Optional Systems**:
    *   **Fog of War**: Create a GameObject with the `FogOfWarLite` component.
    *   **Minimap**: Requires a dedicated Camera and `RTSMinimapController`.

### Setup for Civil Crowds

For ambient NPCs and city crowds:

1.  **Controller Settings**:
    *   Ensure your `EnemyCrowd` asset has `Civil Mode` enabled and `Combat System` disabled.
2.  **Navigation**:
    *   Add `CivilWaypoint` or `CivilZone` objects to your scene to define where crowds can walk.
    *   The `EnemyMassesCrowdController` will automatically detect these if configured in the Crowd asset.

## 6. Next Steps

Once the wizard is complete:
1.  Open the **Demo Scene** to see the system in action.
2.  Select the **EnemyMassesCrowdController** in your scene to tweak spawn rates and limits.
3.  Review the `EnemyCrowd` ScriptableObject created by the wizard to further customize your unit's stats and behavior.

Enjoy creating massive battles!
