import { Run, type Animator } from './animator';
import type { GoldBurst, Floaters, ScreenPoint } from './effects';
import type { FlightState } from './flight';
import { cellCenter, cellYaw, edgeNeighbours } from './geometry';
import type { PlacementGlow } from './glow';
import type { Rack } from './rack';
import type { Sound } from './sound';
import { durations, easing, scaled } from './timing';
import type { TileLayer } from './tiles';
import type { DealView, DrawnEvent, FinishedEvent, GhostPose, MoveView, PassedEvent, PlacedEvent, TileView } from './types';

/** Height a tile falls from when there was no ghost tile to start from. */
const DROP_FROM = 0.9;

/** The colour of the back of a tile. */
const TINT_BACK = 0xd6c9aa;

/** What the director needs from the scene. */
export interface DirectorHost {
  readonly animator: Animator;
  readonly rack: Rack;
  /** Holds the tile that is being placed while it falls and bounces. */
  readonly mover: TileLayer;
  readonly floaters: Floaters;
  readonly burst: GoldBurst;
  readonly flash: PlacementGlow;
  readonly sound: Sound;
  /** Sparkles per gold burst for the current quality. */
  readonly sparkles: number;
  setBoard(tiles: readonly TileView[]): void;
  /** A tile landed: the next frame redraws the shadow map. */
  shadowChanged(): void;
  /** Takes over the ghost tile if the player let go of one; the ghost disappears. */
  takeGhost(): GhostPose | null;
  /** The centre of a named element of the page (the pot, a player), in canvas pixels. */
  anchor(name: string): ScreenPoint | null;
  /** A point above the tile on a cell, in canvas pixels. */
  aboveCell(row: number, col: number): ScreenPoint;
  /** The middle of the top of the rack strip, in canvas pixels. */
  rackTop(): ScreenPoint;
  /** The centre of a tile's slot in the rack, in canvas pixels. */
  slotPoint(tileId: number): ScreenPoint;
  /**
   * Plans a camera move that brings a cell into view; null when it is in view already. The plan
   * takes the progress of the move (0 to 1) and does nothing once the player has taken the camera.
   */
  planFollow(row: number, col: number): ((progress: number) => void) | null;
  /** Side of a tile in the rack, in pixels. */
  rackTileSize(): number;
  canvasSize(): { width: number; height: number };
  flyTiles(states: readonly FlightState[]): void;
  clearFlights(): void;
  /** The animations are done; C# may show the next screen. */
  ready(): void;
  /** Notes what happened, for automated tests. */
  log(entry: string): void;
}

/**
 * Plays what C# tells the scene about a move: the tile that falls on the table, the points that
 * float up, the tiles that fly in from the pot, the rack that slides away and the settlement at
 * the end of a round. All timing and easing is here and in timing.ts; C# only says what happened
 * and is told when everything has settled.
 */
export class Director {
  private current: Promise<void> | null = null;
  private run: Run | null = null;
  private pending: Promise<void>[] = [];
  private disposed = false;

  constructor(private readonly host: DirectorHost) {}

  /** True from the start of a move or deal until the scene has told C# that it is done. */
  get busy(): boolean {
    return this.current !== null;
  }

  /** Plays one move; the returned promise resolves after ready() was called. */
  async playMove(move: MoveView): Promise<void> {
    await this.begin(async run => {
      for (const event of move.events) {
        switch (event.kind) {
          case 'placed':
            await this.place(event, move, run);
            break;
          case 'drawn':
            await this.draw(event, move, run);
            break;
          case 'passed':
            await this.pass(event, run);
            break;
          case 'finished':
            await this.settle(event, run);
            break;
        }
      }

      await this.conclude(move, run);
    });
  }

  /** Deals the tiles of a new round from the pot to the players, face down. */
  async deal(view: DealView): Promise<void> {
    await this.begin(async run => {
      this.host.log('deal');
      await this.dealTiles(view, run);
    });
  }

  /** Jumps to the end of whatever is playing. */
  finish(): void {
    this.run?.finish();
    this.host.floaters.finishAll();
  }

  /** Runs the action once nothing is playing any more; finishes what is playing first. */
  whenIdle(action: () => void): void {
    if (this.current === null) {
      action();
      return;
    }

    this.finish();
    void this.current.then(action);
  }

  dispose(): void {
    this.disposed = true;
    this.finish();
  }

  private async begin(sequence: (run: Run) => Promise<void>): Promise<void> {
    if (this.current !== null) {
      this.finish();
      await this.current;
    }

    const run = new Run(this.host.animator);
    this.run = run;
    this.pending = [];
    const done = (async () => {
      try {
        await sequence(run);
      } catch (error) {
        // Whatever went wrong, C# must not wait for ever.
        console.error('Animation failed', error);
      } finally {
        this.host.mover.setTiles([]);
        this.host.clearFlights();
        this.run = null;
        this.current = null;
        if (!this.disposed) {
          this.host.ready();
        }
      }
    })();
    this.current = done;
    await done;
  }

  // --- A tile is laid -----------------------------------------------------------------------

  private async place(event: PlacedEvent, move: MoveView, run: Run): Promise<void> {
    const host = this.host;
    const ghost = host.takeGhost();
    const fromGhost = ghost !== null && ghost.row === event.row && ghost.col === event.col;
    const startLift = fromGhost ? ghost.lift : DROP_FROM;
    const startTint = fromGhost ? ghost.tint : 0xffffff;
    host.rack.hide(event.tileId);
    host.log('placed');

    const show = (lift: number, tint = 0xffffff): void =>
      host.mover.setTiles([{ row: event.row, col: event.col, values: event.values, lift, tint }], true);
    // The tiles it comes down against: they make it click.
    const touching = edgeNeighbours(event.row, event.col).filter(n => move.board.some(t => t.row === n.row && t.col === n.col)).length;
    show(startLift, startTint);

    await run.animate(durations.drop.fall, t => show(startLift * (1 - t), blend(startTint, 0xffffff, t)), { ease: easing.inQuad });

    // The tile lands. The shadow map is redrawn once, now, and not while the tile bounces.
    host.shadowChanged();
    host.log('landed');
    host.sound.land(1, touching);
    this.celebrate(event, run);

    // Every touch of the table is heard, softer each time.
    const [first, second] = durations.bounceHeights;
    await run.animate(durations.drop.bounce, t => show(first * easing.parabola(t)));
    host.sound.land(0.4);
    await run.animate(durations.drop.settle, t => show(second * easing.parabola(t)));
    host.sound.land(0.18);

    host.mover.setTiles([]);
    host.setBoard(move.board);
  }

  /** Floats the points up, and adds the gold effect for a bridge or a hexagon. */
  private celebrate(event: PlacedEvent, run: Run): void {
    const host = this.host;
    host.log(`points:${event.total}`);
    this.pending.push(host.floaters.score(host.aboveCell(event.row, event.col), event.lines, event.total));
    if (!event.lines.some(line => line.gold)) {
      return;
    }

    host.log('gold');
    host.sound.bonus(event.lines.filter(line => line.gold).length);
    const center = cellCenter(event.row, event.col);
    const golds = event.lines.filter(line => line.gold).length;
    host.burst.emit(center.x, center.y, host.sparkles, 0.9 + 0.35 * golds);
    // A hexagon flashes all of its cells; a bridge flashes the tile that made it.
    const cells = event.hexagon.length > 0 ? event.hexagon : [{ row: event.row, col: event.col }];
    const flash = (size: number): void => host.flash.setCells(cells.map(c => ({ row: c.row, col: c.col, scale: size })));
    flash(1);
    this.pending.push(
      run
        .animate(durations.burst, t => {
          host.burst.setProgress(t);
          host.flash.setBrightness(1.5 * (1 - t) ** 2);
          flash(1 + 0.7 * easing.outCubic(t));
        })
        .then(() => host.flash.setCells([])),
    );
  }

  // --- A tile is drawn ----------------------------------------------------------------------

  private async draw(event: DrawnEvent, move: MoveView, run: Run): Promise<void> {
    const host = this.host;
    const tile = move.rack.find(t => t.id === event.tileId);
    if (!tile) {
      return;
    }

    // The slot is reserved at once; the tile shows up in it when it has flown in.
    host.rack.setTiles(move.rack, [event.tileId]);
    host.log('drawn');
    const pot = host.anchor('pot') ?? host.rackTop();
    const to = host.slotPoint(event.tileId);
    const size = host.rackTileSize();
    const yaw = cellYaw(0, host.rack.slotOf(event.tileId));
    const arc = Math.max(70, Math.hypot(to.x - pot.x, to.y - pot.y) * 0.4);

    this.pending.push(host.floaters.note({ x: pot.x, y: pot.y - 20 }, `−${event.penalty}`, 'penalty'));
    host.sound.flight(scaled(durations.flight));
    await run.animate(
      durations.flight,
      t => {
        const turn = easing.inOutCubic(clamp01((t - 0.2) / 0.65));
        host.flyTiles([
          {
            x: lerp(pot.x, to.x, t),
            y: lerp(pot.y, to.y, t) - Math.sin(Math.PI * t) * arc,
            size: lerp(size * 0.7, size, t) * (1 + 0.3 * Math.sin(Math.PI * t)),
            yaw: yaw + (1 - t) * 0.9,
            flip: 1 - turn,
            values: tile.values,
            tint: turn < 0.5 ? TINT_BACK : undefined,
          },
        ]);
      },
      { ease: easing.inOutCubic },
    );

    host.clearFlights();
    host.rack.reveal(event.tileId);
    // In the rack it comes down between its neighbours.
    host.sound.land(0.3, 1);
  }

  // --- A player passes ----------------------------------------------------------------------

  private async pass(event: PassedEvent, run: Run): Promise<void> {
    this.host.log('passed');
    this.pending.push(this.host.floaters.note(this.host.rackTop(), `−${event.penalty}`, 'penalty'));
    await run.pause(0.3);
  }

  // --- The round ends -----------------------------------------------------------------------

  /** Shows what the tiles left in the other racks are worth, one rack after the other. */
  private async settle(event: FinishedEvent, run: Run): Promise<void> {
    const host = this.host;
    host.log('finished');
    await this.hideRack(run);
    if (event.winner !== null && event.goingOut) {
      const at = host.anchor(`player-${event.winner}`) ?? host.rackTop();
      this.pending.push(host.floaters.note(at, `+${event.goingOut.points}  ${event.goingOut.label}`, 'gold', 2.4));
      host.sound.win();
      await run.pause(0.5);
    }

    for (const step of event.rivals) {
      host.log(`settle:${step.name}`);
      host.rack.setTiles(step.tiles);
      host.rack.setSlide(1);
      await this.slideRack(run, 1, 0);
      this.pending.push(host.floaters.note(host.rackTop(), `+${step.line.points}  ${step.line.label}`, 'gold', durations.settlementHold));
      await run.pause(durations.settlementHold);
      await this.hideRack(run);
    }
  }

  // --- The end of every move ----------------------------------------------------------------

  private async conclude(move: MoveView, run: Run): Promise<void> {
    const host = this.host;
    if (move.rack.length === 0) {
      // The turn passes on (or the round is over): the rack slides away before the next screen.
      if (host.rack.visible) {
        await run.pause(durations.beforeHandover);
        await this.hideRack(run);
      }

      await Promise.all(this.pending);
    }

    // The camera follows the last tile laid, after its points have faded, so the next player sees it.
    const placed = move.events.filter((event): event is PlacedEvent => event.kind === 'placed').at(-1);
    const follow = placed ? host.planFollow(placed.row, placed.col) : null;
    if (follow) {
      await run.animate(durations.follow, follow, { ease: easing.inOutCubic });
    }

    host.setBoard(move.board);
  }

  private async hideRack(run: Run): Promise<void> {
    const rack = this.host.rack;
    if (!rack.visible) {
      return;
    }

    await this.slideRack(run, 0, 1);
    rack.setTiles([]);
    rack.setSlide(0);
  }

  private slideRack(run: Run, from: number, to: number): Promise<void> {
    return run.animate(durations.rackSlide, t => this.host.rack.setSlide(lerp(from, to, t)), { ease: easing.inOutCubic });
  }

  // --- The deal -----------------------------------------------------------------------------

  private async dealTiles(view: DealView, run: Run): Promise<void> {
    const host = this.host;
    const pot = host.anchor('pot') ?? host.rackTop();
    const { height } = host.canvasSize();
    const size = Math.min(height * 0.1, 84);

    interface Dealt {
      to: ScreenPoint;
      delay: number;
      t: number;
      spin: number;
    }

    const dealt: Dealt[] = [];
    const rounds = Math.max(0, ...view.counts);
    for (let round = 0; round < rounds; round++) {
      view.counts.forEach((count, player) => {
        if (round < count) {
          const to = host.anchor(`player-${player}`) ?? host.rackTop();
          dealt.push({ to, delay: dealt.length * durations.dealStagger, t: 0, spin: (player % 2 === 0 ? 1 : -1) * (0.6 + 0.4 * (round % 3)) });
        }
      });
    }

    host.sound.deal(dealt.length, scaled(durations.dealStagger));
    const lastDelay = dealt.length === 0 ? 0 : dealt[dealt.length - 1].delay;
    const render = (): void => {
      host.flyTiles(
        dealt
          .filter(d => d.t > 0 && d.t < 1)
          .map(d => ({
            x: lerp(pot.x, d.to.x, d.t),
            y: lerp(pot.y, d.to.y, d.t) - Math.sin(Math.PI * d.t) * 60,
            size: lerp(size, size * 0.5, d.t),
            yaw: d.spin * d.t * 2.2,
            flip: 1,
            values: [0, 0, 0] as const,
            tint: TINT_BACK,
          })),
      );
    };

    // Every tile has a tween of its own that only stores its progress; the last tween draws them all.
    const tweens = dealt.map(d => run.animate(durations.flight, t => (d.t = t), { delay: d.delay, ease: easing.inOutCubic }));
    tweens.push(run.animate(durations.flight + lastDelay, render));
    await Promise.all(tweens);
    host.clearFlights();
  }
}

function lerp(a: number, b: number, t: number): number {
  return a + (b - a) * t;
}

function clamp01(value: number): number {
  return Math.min(Math.max(value, 0), 1);
}

/** Mixes two 0xRRGGBB colours. */
function blend(from: number, to: number, t: number): number {
  const channel = (shift: number): number => Math.round(lerp((from >> shift) & 0xff, (to >> shift) & 0xff, t));
  return (channel(16) << 16) | (channel(8) << 8) | channel(0);
}
