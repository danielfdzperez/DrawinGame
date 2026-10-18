<div align="center">

# DrawinGame

**A platformer where you draw the level by hand on a sheet of paper.**

![Godot](https://img.shields.io/badge/Godot-4.x_.NET-478CBF?style=for-the-badge&logo=godotengine&logoColor=white)
![C#](https://img.shields.io/badge/C%23-.NET_8-239120?style=for-the-badge&logo=dotnet&logoColor=white)
![OpenCV](https://img.shields.io/badge/OpenCvSharp4-vision-5C3EE8?style=for-the-badge&logo=opencv&logoColor=white)

![Platform](https://img.shields.io/badge/Windows-0078D6?logo=windows&logoColor=white)
![Status](https://img.shields.io/badge/status-in_development-orange)
[![License](https://img.shields.io/github/license/danielfdzperez/DrawinGame)](LICENSE)
[![Last commit](https://img.shields.io/github/last-commit/danielfdzperez/DrawinGame)](https://github.com/danielfdzperez/DrawinGame/commits)
[![Stars](https://img.shields.io/github/stars/danielfdzperez/DrawinGame?style=social)](https://github.com/danielfdzperez/DrawinGame/stargazers)
[![Built with AI assistance](https://img.shields.io/badge/Built_with_help_from-Claude_(AI)-D97757?logo=claude&logoColor=white)](#ai-disclosure)

<br>

<img src="docs/draw1.gif" alt="DrawinGame demo: a drawing on paper becomes the game level" width="720">

</div>

---

## What is it?

You draw the level on a sheet of paper with a marker, point a **webcam** at it, and the game builds the map in **real time**. If you change the drawing, the level updates instantly.

| Stroke color | Meaning |
|---|---|
| Black | Platforms |
| Red | Hazards *(on the roadmap)* |

## How it works

```mermaid
flowchart LR
    A[Webcam] --> B[4-corner area<br/>+ perspective correction]
    B --> C[Stroke detection<br/>threshold + local contrast]
    C --> D[Contours to polygons<br/>Douglas-Peucker]
    D --> E[Frame-to-frame<br/>stabilization]
    E --> F[Godot<br/>level collisions]
```

- Collisions are **polygons with vertices** (simplified contours), never a pixel grid.
- The camera and vision code run on a **separate thread** and hand the polygons to Godot through a thread-safe structure.
- You can test it **without a webcam** by loading an image.

## Repository structure

| Folder | Contents |
|---|---|
| [`godot/`](godot) | The game project (Godot 4 .NET, C#) |
| [`prototype/`](prototype) | Vision prototype (WinForms + OpenCvSharp). See its [README](prototype/README.md) |
| [`AIDocs/`](AIDocs) | Technical documentation: vision, stabilization, decisions and roadmap (in Spanish) |

## Try it

### Requirements
- Windows 10/11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Godot 4.x **.NET** edition](https://godotengine.org/download/windows/)
- A webcam (optional, an image-loading mode is available)

### Vision prototype
```bash
cd prototype
dotnet run
```

### Game
1. Open Godot and click **Import**.
2. Select `godot/project.godot`.
3. Click **Build** (hammer icon), then press **F5**.

## Roadmap

- [x] Detect black drawings on white paper and turn them into polygons
- [x] Thin strokes (local contrast)
- [x] Frame-to-frame shape stabilization
- [x] Area selection with 4 corners and perspective correction
- [x] Godot project with player, camera/vision and screen flow
- [ ] Rebuild the level when the drawing changes without losing player state
- [ ] Red strokes as hazards
- [ ] Persistent calibration
- [ ] Windows export

More detail in [`AIDocs/roadmap.md`](AIDocs/roadmap.md).

## AI disclosure

This project was built **with AI assistance**: [Claude](https://claude.ai) (Anthropic) was used as an assistant to write and review code and documentation. Design decisions, testing and the direction of the project are the author's.

## License

[MIT](LICENSE) © 2026 Daniel Fernandez
