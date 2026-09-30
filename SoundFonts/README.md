# SoundFonts

Put piano `.sf2` files here. KeySound lists them in its **Sound** menu. Until you add one, it uses its basic built-in piano. SoundFont files are excluded from git.

KeySound plays preset 0 of the SoundFont, which in General MIDI SoundFonts is Acoustic Grand Piano. When a SoundFont loads, KeySound measures how loud it plays at each velocity. It then adjusts velocities so soft to loud spans about 30 dB, which keeps soft notes audible.

## Recommended: Salamander Grand Piano
A Yamaha C5 grand with 16 velocity layers. License: CC-BY 3.0, by Alexander Holm.

1. Download [SalamanderGrandPiano-SF2-V3+20200602.tar.xz](https://freepats.zenvoid.org/Piano/SalamanderGrandPiano/SalamanderGrandPiano-SF2-V3+20200602.tar.xz) (296 MB) from [FreePats](https://freepats.zenvoid.org/Piano/acoustic-grand-piano.html).
2. Extract it with 7-Zip. The `tar` built into Windows can't read `.xz` files.
3. Put `SalamanderGrandPiano-V3+20200602.sf2` (1.27 GB) in this folder.

## Smaller alternatives from the same page
- **Upright Piano KW:** a Kawai upright, 27 MB, public domain (CC0).
- **YDP Grand Piano:** a Yamaha Disklavier grand, 36 MB, CC-BY 3.0.
