# Music and sound effects

When a new screen opens with a looping Sound control that autoplays the same song as one still playing (a title theme carried into the next screen), the song carries on from where it is instead of starting again. Give that control no delay. In web and desktop apps, songs a Sound control plays stream while they play; short sound effects are decoded when the game opens so they play instantly. See [[How big should a game be?|Web-and-Desktop-Apps#how-big-should-a-game-be]].

Arcadia Studio makes its own 8-bit sounds: songs in the **Music maker** and game sounds in the **Sound effect maker**. Both save into **Assets** as ordinary sounds, so you can use them anywhere a sound goes: a Sound control, the *Play sound* action, or `ctx.client.playSound('myproject:theme')` in a script. Each sound also keeps what made it, beside the file, so you can open it again and change it. That file is saved with the project but never exported.

Open them from **Project → Music maker…** and **Project → Sound effect maker…**, the **Music maker** and **Sound effects** buttons in Assets, or by right-clicking a sound in Assets.

Sounds save as **Ogg** by default: it's small and plays everywhere, including Minecraft. **WAV** is also available for web and desktop apps.

For spoken lines (a character, a narrator, a menu voice), use **Advanced → Create audio from text…**. See [[Speech and video|Speech-and-Video]].

## The Music maker

A song is a set of **layers**, like the tracks in a music program. Each layer has its own instrument and notes.

**Top bar**
- ▶ **Play** starts from the green marker; click the ruler to move it. ■ **Stop** stops, and ● **Record** records.
- **Loop** repeats while you listen. **Metronome** clicks on every beat and counts you in before recording.
- **Tempo** (BPM), **Time** (2/4, 3/4, 4/4, 6/8 and so on), and **Key** and scale set the song up. Notes in the scale are tinted in the piano roll and marked with a dot on the keyboard.
- **Bars** sets the song's length; **Fit** trims it to the notes. **Grid** is the smallest step: 1/8, 1/16 or 1/32 notes.
- **New notes** sets the length drawn by a click, and **Velocity** sets how loud new or selected notes are.
- **Seamless loop** saves the song so it repeats without a gap, which is right for background music. Notes that ring past the end carry on at the start.
- **Sheet music…**, **Import MIDI…**, **Export MIDI…**, **Song from audio…** and **Export audio…** are covered below.

**Keyboard**
Click the on-screen piano, or play your computer keyboard. **Z S X D C V G B H N J M** play one octave (the letters on the second row are the black keys), and **Q 2 W 3 E R 5 T 6 Y 7 U** play the octave above. **Oct −/+** or PageUp/PageDown change octave. Notes play with the selected layer's instrument.

**Recording**
Select a layer and press ● **Record** (F9). After one bar of count-in, play along with the other layers. Every key you press becomes a note, snapped to the grid. Press ■ to stop. **Ctrl+Z** takes a whole take back.

**Layers panel**
Each layer has these settings:
- a name, and **M** to mute it or **S** to solo it (only soloed layers play)
- an **instrument** (see below), and **Edit…** to change it
- **Volume**, **Pan**, **Pitch** (moves every note up or down in semitones) and **Fine** (tuning in cents). Double-click a slider to reset it.

Use **+ Add**, **Duplicate**, **Delete** and ▲ ▼ to manage layers. New layers suggest a role (Bass, Drums, Harmony…) with a fitting instrument.

**Piano roll**
Time runs to the right and pitch goes up. The selected layer's notes are solid and the other layers are faint.

| Do this | To |
|---|---|
| Click an empty spot | Add a note (drag right to make it longer) |
| Drag a note | Move it (it plays as you change pitch) |
| Drag a note's right end | Make it longer or shorter |
| Right-click a note | Delete it |
| Shift+drag / Shift+click | Select several |
| ← → ↑ ↓ | Move the selection a step or a semitone (Shift+↑↓: an octave) |
| Ctrl+A, Ctrl+C, Ctrl+V, Ctrl+D | Select all, copy, paste at the green marker, duplicate after itself |
| Delete | Delete the selection |
| Ctrl+wheel, − + | Zoom |
| Space | Play / stop |

For a **Drum kit** layer, the rows are named after the drums: kick C2, snare D2, clap D#2, closed hat F#2, open hat A#2, crash C#3, ride D#3, and toms F2, A2 and C3.

## Instruments

| Preset | Sound |
|---|---|
| Square lead, Pulse 25%, Pulse 12% | Classic chip leads, from hollow to thin and nasal |
| Triangle bass | The console bass line |
| Saw brass, Sine flute, Pluck, Bell | Other colours |
| Chip chord, Minor chord | A note played as a fast arpeggio, the classic 8-bit "chord" |
| Laser bass | A saw that drops an octave at the start of each note |
| Soft pad | Slow, soft, with echo |
| Piano, Organ, Guitar, Harp, Strings, Choir, Music box | 8-bit takes on familiar instruments (MIDI files use these) |
| Noise, Drum kit | Percussion |

**Edit…** opens the instrument's knobs, with a picture of the sound and buttons to hear a note, a chord, a scale or drums:
- **Wave**: square (with pulse width), a stepped triangle like the NES, saw, sine, noise, or drums
- **Envelope**: attack, decay, sustain and release
- **Pitch**: vibrato (depth, speed, delay), a slide at the start of each note, an arpeggio such as `0,4,7` with its speed, and octave
- **Colour**: bit crush, echo (time and feedback) and volume

## SoundFonts

The **Sounds** dropdown in the Music maker plays the song with the built-in **Chip** sounds, or with a **SoundFont**: a bank of recorded instruments (pianos, guitars, strings, brass, drum kits…). With a SoundFont, each layer's instrument list shows the font's instruments by their General MIDI number and name, then its drum kits. Layers remember their instrument by that number, so a song keeps its piano, guitar and drums when you switch to another font, and imported MIDI files keep the instruments they were written for.

**GeneralUser GS** comes with Arcadia Studio. **Get more soundfonts…** at the bottom of the dropdown opens the SoundFont library, where each font has a description, its size and its licence:

| Font | Size | Sound | Licence |
|---|---|---|---|
| GeneralUser GS | 32 MB | Balanced, clean all-rounder (included) | Free for any use |
| FluidR3 GM | 151 MB | Full, realistic orchestral and band instruments | MIT |
| FatBoy | 320 MB | Big, punchy, tuned for classic DOS game music | CC BY-SA 3.0 (credit it) |
| Arachno | 155 MB | Famous synth sounds, great for 90s game soundtracks | Personal use; not recommended for commercial projects |

**Download** gets a font once; it's kept on this computer and shared by every project. **Import custom soundfont (.sf2)…** adds any SoundFont 2 file you have. **Use** plays the current song with a font, and **Remove** deletes a downloaded or imported one.

Live playback, the keyboard, saving and **Export audio…** all use the song's font. A saved song is stored as audio, so it plays the same in your game on any computer. If you open a song on a computer that doesn't have its font, the dropdown shows the font as not installed and the song plays with the chip sounds until you get it.

## Sheet music

**Sheet music…** opens the selected layer as a staff, with a clef, key signature and time signature, the notes, and rests in the gaps. It's the same notes as the piano roll, so you can play a layer in on the keyboard and tidy it up here, or the other way round.

- Choose a note value in the toolbox (whole, half, quarter, eighth, sixteenth, thirty-second), optionally **dotted**, then click the staff to place it. Notes follow the key signature; ♮ ♯ ♭ override it.
- Choose **rest** and click to insert a rest; the later notes move along.
- Click a note to select it. ↑/↓ move it a semitone (Shift for an octave), and Delete or right-click removes it.
- Pick the **clef** and the **instrument** here too. ▶ plays the layer alone or with the others.
- **Use these notes** puts your changes into the song, and **Cancel** leaves it as it was.

A note whose length isn't a standard value is drawn as the nearest one, with a thin bar showing its real length.

## MIDI

**Import MIDI…** reads a standard MIDI file (.mid). Each track and channel becomes a layer, and each General MIDI instrument becomes the closest 8-bit preset (channel 10 becomes the Drum kit). The file's tempo, time signature and key come with it. Replace the song, or add its layers to yours. **Export MIDI…** writes your song out for other music programs.

**Bard MIDIs.** The MIDI files FFXIV bards play (from libraries such as XIVMIDI or the Bard Music Player collection) are hand-made arrangements, so they're the most faithful way to get a song. Their tracks are named after the in-game instrument (Harp, Lute, Fiddle, Flute, Trumpet, Timpani, SnareDrum, ElectricGuitarClean…), sometimes with an octave shift such as `Lute+1`. Arcadia Studio reads those names: each track gets the matching instrument and octave, and the percussion tracks play the matching drum.

When importing, choose **Matching instruments** to keep those sounds, or **8-bit chip voices** for a chiptune: the highest part becomes the square-wave lead, low parts the triangle bass, the rest pulse waves, and drums stay on the kit. With **Split solo parts into melody, chords and bass**, a part that plays melody and chords at once (a solo harp arrangement, say) is split into a lead (the notes above the chords), a chord layer and a bass layer.

## Turning a song into 8-bit

**Song from audio…** in the Music maker makes a **chiptune cover** of a recording (MP3, WAV, Ogg, M4A, AAC, WMA or FLAC), the way people arrange songs for chip or bard instruments:

**Split instruments first (best).** The first time you use Song from audio, Arcadia Studio offers a one-time download of **Demucs** (by Meta, MIT licence, 166 MB from Hugging Face, kept in your app data folder so the installer stays small). With it, the song is first split into **vocals, bass, drums and other instruments**, and each part is turned into notes on its own: the singing becomes the melody with a note for every sung syllable, the bass guitar's own line becomes the bass, the drum part gives the beat and drums, and the guitars and keys give the chords. This takes a minute or two on a typical computer, and converting the same song again with other options reuses the split. You can say **Not now** and download it later from the **Download…** button in the Song from audio window. Without it, the song is converted from the full mix as described below.

1. **The beat.** The beat is followed through the whole song, so a band that speeds up or slows down stays in time, and the bar lines are found from where the chords change. Press **Detect** to see the tempo first. If it's counted differently from how you hear it (80 instead of 160, say), halve or double it.
2. **The chords.** The chord on every beat is named (major, minor or power chord) from the guitars and keys. In most band mixes these are panned to the sides, away from the singer.
3. **The melody.** With the split, from the singer's own part. Without it, from the middle of the mix, where the singer usually is, limited to the singing range, and cut down to one clear line.
4. **The drums.** Kick, snare and hi-hat come from their hits.

**Band arrangement** (the default) plays it back as steady **Chords** (on eighth notes, quarter notes or held; as power chords, full triads, or automatically chosen), a **Bass** (the bass guitar's own notes with the split, the chord roots without), the **Melody**, and **Drums**. **Every note heard** keeps everything the note detector found instead, which suits piano or solo pieces better than full bands.

The result is an ordinary song, so you can fix wrong notes in the piano roll, change instruments, open it as sheet music, save it, or export MIDI or MP3.

A singer's words can't be played by a chip, so the voice becomes the melody. That's how chiptune covers of songs sound. Tick **Remove the vocals** to leave the singing out and let the instruments lead. **Melody only** adds just the singer's line as one layer, at the song's current tempo.

Clear pop, rock and game music comes out closest. Busy mixes, heavy reverb and songs without a steady beat need more tidying.

## Recordings

- **Project → Import audio as 8-bit…** offers both ways: **Turn into an 8-bit song** (above), or **Crunch the recording**. Crunching keeps the sound itself, voices included, but lo-fi. Use the **NES**, **Game Boy**, **Atari** or **Arcade** presets, or choose 4–22 kHz and 2–8 bits (go low for the real old-console sound). There's an optional centre-vocal remover, and you can **Hear it** before saving to Assets.
- Right-click a sound in Assets for **Play**, **Add to screen** (a Sound control), **Open in music maker** or **Open in sound effect maker** (for sounds made there), **Turn into an 8-bit song…**, **Crunch to lo-fi…**, **Show in Explorer** and **Save a copy as…**.
- **Export audio…** (in the Music maker and the Sound effect maker) saves the sound anywhere as **MP3**, WAV or Ogg, for sharing outside Arcadia Studio.

## The Sound effect maker

- Start from a preset: **Coin, Jump, Laser, Explosion, Power-up, Hurt, Blip** or **Random**. Each click gives a new take on it.
- **Mutate** gives a close variation of the current sound.
- Shape it with the sliders. The sound plays after every change (untick *Play on every change* to stop that).
  - **Volume shape**: attack, sustain, punch, decay
  - **Pitch**: start and lowest pitch, slide and how the slide changes, vibrato, a pitch jump after a moment ("ding-DING"), and repeat
  - **Pulse**: width and sweep
  - **Tone**: low-pass and high-pass filters, bit crush and volume
- Name it and **Save**. **Save as new** keeps the old one.

## AI assistants

Through [[MCP|MCP-and-AI-Assistants]], an assistant can use `sound_effect` to make effects from the same presets and settings, `compose_music` to write songs as note text such as `"C4 q E4 e G4 e | C5 h r q C4+E4+G4 w"`, and `song_from_audio` to turn a recording into an 8-bit song. `read_music` reads a song back, layer by layer, and the Music maker can open anything they make.
