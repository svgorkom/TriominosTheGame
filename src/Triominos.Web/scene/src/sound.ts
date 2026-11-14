/**
 * The sounds of the table, made with Web Audio when they are played: a tile on wood, a tile
 * sliding out of the pot, dealing, the gold chime of a bonus and the fanfare of a win. Nothing is
 * downloaded (the only buffer is a second of noise made at the first sound), so the page stays
 * small and the sounds can be tuned in code.
 *
 * Browsers only start audio after the player has clicked or pressed a key. The first click or key
 * press anywhere on the page creates the audio context; sounds asked for before that are dropped.
 */
export class Sound {
  private context: AudioContext | null = null;
  private master: GainNode | null = null;
  private noise: AudioBuffer | null = null;
  private unavailable = false;
  private enabled = true;
  private volume = 0.6;
  private readonly lastPlayed = new Map<string, number>();
  private readonly unlockOn = ['pointerdown', 'keydown'] as const;
  private readonly unlockHandler = (): void => this.unlock();

  /** @param log notes every sound that was asked for (and is not muted), for automated tests. */
  constructor(private readonly log: (entry: string) => void) {
    this.unlockOn.forEach(type => window.addEventListener(type, this.unlockHandler, { capture: true }));
  }

  /** The state of the audio context: "none" until the player has clicked, then "running" or "suspended". */
  get state(): string {
    return this.context?.state ?? 'none';
  }

  /**
   * Turns the sounds on or off and sets the volume (0 to 1). With a preview the new level is
   * played once, so the player hears what they chose.
   */
  configure(enabled: boolean, volume: number, preview = false): void {
    this.enabled = enabled;
    this.volume = clamp01(volume);
    if (this.master !== null) {
      // The ear hears loudness roughly with the square root of the gain, so the slider is squared.
      this.master.gain.value = this.volume * this.volume;
    }

    if (preview) {
      this.land(0.8);
    }
  }

  /** A tile hits the table. Strength 1 is the fall, smaller values are the bounces. */
  land(strength: number): void {
    const ctx = this.begin('land', 0.03);
    if (ctx === null) {
      return;
    }

    const at = ctx.currentTime;
    const s = clamp01(strength);
    const pitch = 0.92 + Math.random() * 0.16;
    this.noiseBurst(ctx, at, { freq: 2200 * pitch, q: 0.9, duration: 0.03, gain: 0.5 * s });
    this.tone(ctx, at, { freq: 210 * pitch, endFreq: 110 * pitch, type: 'sine', duration: 0.14, gain: 0.9 * s });
    this.tone(ctx, at, { freq: 640 * pitch, endFreq: 420 * pitch, type: 'triangle', duration: 0.05, gain: 0.22 * s });
  }

  /** A tile slides from the pot to the rack; the swish lasts as long as the flight. */
  flight(seconds: number): void {
    const ctx = this.begin('draw', 0.1);
    if (ctx === null) {
      return;
    }

    const at = ctx.currentTime;
    const duration = Math.max(seconds, 0.08);
    const source = this.noiseSource(ctx);
    const filter = ctx.createBiquadFilter();
    filter.type = 'bandpass';
    filter.Q.value = 1.1;
    filter.frequency.setValueAtTime(500, at);
    filter.frequency.exponentialRampToValueAtTime(2600, at + duration);
    const envelope = ctx.createGain();
    envelope.gain.setValueAtTime(0.0001, at);
    envelope.gain.linearRampToValueAtTime(0.2, at + duration * 0.4);
    envelope.gain.exponentialRampToValueAtTime(0.0001, at + duration);
    source.connect(filter).connect(envelope).connect(this.master!);
    source.start(at, Math.random() * 0.5);
    source.stop(at + duration + 0.05);
  }

  /** Tiles are dealt: one flick per tile, `interval` seconds apart. */
  deal(count: number, interval: number): void {
    const ctx = this.begin('deal', 0.2);
    if (ctx === null) {
      return;
    }

    const at = ctx.currentTime;
    for (let i = 0; i < count; i++) {
      const pitch = 0.9 + Math.random() * 0.2;
      this.noiseBurst(ctx, at + i * interval, { freq: 3200 * pitch, q: 1.5, duration: 0.02, gain: 0.2 });
    }
  }

  /** A bridge or hexagon: a gold chime that gets longer with every gold line of the move. */
  bonus(goldLines: number): void {
    const ctx = this.begin('bonus', 0.3);
    if (ctx === null) {
      return;
    }

    const at = ctx.currentTime;
    const notes = [784, 988, 1175, 1568, 1976];
    const count = Math.min(2 + Math.max(goldLines, 1), notes.length);
    for (let i = 0; i < count; i++) {
      this.bell(ctx, at + i * 0.085, notes[i], 0.2, 0.8 + 0.25 * i);
    }
  }

  /** The player goes out: a rising run of bells and a chord. */
  win(): void {
    const ctx = this.begin('win', 0.5);
    if (ctx === null) {
      return;
    }

    const at = ctx.currentTime;
    [523, 659, 784, 1047, 1319].forEach((freq, i) => this.bell(ctx, at + i * 0.13, freq, 0.24, 1.1));
    [523, 784, 1047].forEach(freq => this.bell(ctx, at + 0.75, freq, 0.17, 2));
  }

  dispose(): void {
    this.unlockOn.forEach(type => window.removeEventListener(type, this.unlockHandler, { capture: true }));
    void this.context?.close().catch(() => undefined);
    this.context = null;
    this.master = null;
  }

  /** Creates the audio context (once) and wakes it up; this has to happen in a click or key press. */
  private unlock(): void {
    if (this.context === null && !this.unavailable) {
      try {
        const context = new AudioContext({ latencyHint: 'interactive' });
        const master = context.createGain();
        master.gain.value = this.volume * this.volume;
        // Several sounds at once (a bonus on top of a landing) must not clip.
        const limiter = context.createDynamicsCompressor();
        master.connect(limiter).connect(context.destination);
        this.context = context;
        this.master = master;
      } catch {
        // No Web Audio here: the game stays silent.
        this.unavailable = true;
      }
    }

    void this.context?.resume().catch(() => undefined);
  }

  /**
   * Starts a sound: notes it in the log when it is not muted, and returns the context to play it
   * in, or null while the sound is muted, the context is not running yet, or the same sound
   * played less than `gap` seconds ago (an animation that is skipped fires its sounds at once).
   */
  private begin(name: string, gap: number): AudioContext | null {
    if (!this.enabled || this.volume <= 0) {
      return null;
    }

    this.log(`sound:${name}`);
    const ctx = this.context;
    if (ctx === null || this.master === null || ctx.state !== 'running') {
      return null;
    }

    const last = this.lastPlayed.get(name);
    if (last !== undefined && ctx.currentTime - last < gap) {
      return null;
    }

    this.lastPlayed.set(name, ctx.currentTime);
    return ctx;
  }

  private noiseSource(ctx: AudioContext): AudioBufferSourceNode {
    if (this.noise === null) {
      this.noise = ctx.createBuffer(1, ctx.sampleRate, ctx.sampleRate);
      const data = this.noise.getChannelData(0);
      for (let i = 0; i < data.length; i++) {
        data[i] = Math.random() * 2 - 1;
      }
    }

    const source = ctx.createBufferSource();
    source.buffer = this.noise;
    source.loop = true;
    return source;
  }

  /** A short burst of filtered noise: the click of wood on wood. */
  private noiseBurst(ctx: AudioContext, at: number, shape: { freq: number; q: number; duration: number; gain: number }): void {
    const source = this.noiseSource(ctx);
    const filter = ctx.createBiquadFilter();
    filter.type = 'bandpass';
    filter.frequency.value = shape.freq;
    filter.Q.value = shape.q;
    source.connect(filter).connect(this.envelope(ctx, at, shape.duration, shape.gain));
    source.start(at, Math.random() * 0.5);
    source.stop(at + shape.duration + 0.05);
  }

  /** A tone that may glide to another pitch and dies away. */
  private tone(ctx: AudioContext, at: number, shape: { freq: number; endFreq?: number; type: OscillatorType; duration: number; gain: number }): void {
    const oscillator = ctx.createOscillator();
    oscillator.type = shape.type;
    oscillator.frequency.setValueAtTime(shape.freq, at);
    if (shape.endFreq !== undefined) {
      oscillator.frequency.exponentialRampToValueAtTime(shape.endFreq, at + shape.duration);
    }

    oscillator.connect(this.envelope(ctx, at, shape.duration, shape.gain));
    oscillator.start(at);
    oscillator.stop(at + shape.duration + 0.05);
  }

  /** A struck bell: the note and two quicker fading overtones. */
  private bell(ctx: AudioContext, at: number, freq: number, gain: number, decay: number): void {
    this.tone(ctx, at, { freq, type: 'sine', duration: decay, gain });
    this.tone(ctx, at, { freq: freq * 2.76, type: 'sine', duration: decay * 0.5, gain: gain * 0.25 });
    this.tone(ctx, at, { freq: freq * 5.4, type: 'sine', duration: decay * 0.25, gain: gain * 0.1 });
  }

  /** A gain that jumps up and dies away exponentially, connected to the master gain. */
  private envelope(ctx: AudioContext, at: number, duration: number, gain: number): GainNode {
    const node = ctx.createGain();
    node.gain.setValueAtTime(0.0001, at);
    node.gain.linearRampToValueAtTime(Math.max(gain, 0.0002), at + 0.003);
    node.gain.exponentialRampToValueAtTime(0.0001, at + duration);
    node.connect(this.master!);
    return node;
  }
}

function clamp01(value: number): number {
  return Math.min(Math.max(value, 0), 1);
}
