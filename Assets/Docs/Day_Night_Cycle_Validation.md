# Day-Night Cycle Validation

Use this note for issue #23 when reviewing whether the cycle affects gameplay readability, not only the skybox.

## Runtime Feedback Added

- Day, dusk, night, dawn, and twilight factors are published as global shader values from `DayNightSkyboxController`.
- A runtime moon fill light is created in Play Mode when no moon light is assigned, giving night scenes low-cost directional shape without serializing scene objects.
- Fog density and range now change across day, dusk, night, and dawn instead of only changing fog color.
- Ambient intensity receives a small night visibility lift so important silhouettes remain readable without flattening the scene.
- `SimpleLight` objects scale active light intensity and emissive output from the day-night emission boost, so powered RV/interior/marker lights read stronger at dusk and night.

## Manual Route

1. Open `Assets/Scenes/Main_Persistent.unity`.
2. Run `Cementery > Performance > Start Visual Evidence Route`.
3. Review the generated screenshots in `Application.persistentDataPath/visual-evidence-route`:
   - `visual-evidence-01-day-clear.png`
   - `visual-evidence-02-sunset-haze.png`
   - `visual-evidence-03-night-fog.png`
   - `visual-evidence-04-dawn-fog.png`
4. Run `Cementery > Performance > Generate Visual Performance Report` and confirm `Assets/Docs/Visual_Performance_Last_Run.md` reports passing day/night and fog route coverage.

## Review Notes

- Day should keep long-range visibility and bright directional sun.
- Dusk should show warmer fog, stronger powered emissives, and reduced direct sunlight.
- Night should retain mood through darker sky/fog while silhouettes remain readable from moon fill and a small ambient boost.
- Dawn should sit between night and day with softer fog and increasing daylight.
- The route records profiler and sampler evidence, but screenshot review is still required before claiming final visual polish.
