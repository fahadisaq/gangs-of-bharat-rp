# 14. Art and Audio Pipeline

There is **no 3D budget**. The pipeline uses free assets, scripts and AI tools.

## 3D (zero budget)

| Source | Use | License |
| --- | --- | --- |
| Quaternius | Low-poly characters, vehicles, props | CC0 |
| Kenney | City kits, props, UI | CC0 |
| Poly Haven | PBR textures, HDRIs, some models | CC0 |
| Mixamo | Character rigging and animations | Free with an Adobe account (royalty-free use in games) |
| Procedural Blender Python | Buildings, chawls, roads, shop fronts, autos, parody vehicle bodies, modular interiors | Our own |
| Free image-to-3D (Hunyuan3D, TRELLIS) | **Hero items** only (key props, statues, special vehicles) | Check each model's license; needs a GPU machine |

## 2D (Gemini)

Gemini image generation makes **textures**, **shop signs and billboards** (parody brands, Hindi and English), **UI icons** and **story stills** for comic-panel cutscenes. Generated images are reviewed by a person before use.

## Dialogue and voice (Sarvam)

| Sarvam capability | Use |
| --- | --- |
| Dialogue generation (LLM) | Story lines, NPC lines, side missions in Hindi and English |
| TTS | Voiced story, cutscenes, NPCs, AI news anchor |
| Translation | English <-> Hindi for UI, subtitles and auto-translate chat |
| STT | Voice moderation and voice commands |

API keys are read from environment variables (`SARVAM_API_KEY`, `GEMINI_API_KEY`) and never stored in the repo.

## Music

**Royalty-free music only** for car radio stations and menus. No copyrighted Bollywood songs.

## Honest quality caveats

- Free assets and AI tools can reach a **good stylized look**, not the photoreal quality of big-budget games.
- Mixed asset sources need one art style guide (colour palette, texture density, shader set) or the city will look inconsistent.
- AI-generated 3D needs manual cleanup (topology, UVs, LODs) and does not replace a 3D artist for main characters.
- A paid 3D artist later is likely needed to match Grand Mobile quality on hero content.

## Device presets

| Preset | Target devices | Settings |
| --- | --- | --- |
| Ultra | Recent flagship phones | Highest draw distance, shadows, reflections, 60 fps where possible |
| High | High-end phones (main target) | High textures, real-time shadows near the player, 30-60 fps |
| Medium | Mid-range phones | Medium textures, fewer shadows, lower draw distance, 30 fps |
| Low | Older phones | Low textures, no real-time shadows, reduced crowds and traffic, 30 fps |
