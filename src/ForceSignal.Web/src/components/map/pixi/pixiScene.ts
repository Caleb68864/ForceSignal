/**
 * The canvas half of the tactical map.
 *
 * Draws what it is handed and nothing else: ship poses and running effects come from `frameAt`,
 * positions from the snapshot, and the outcome of a shot from its FiringResult via the FX registry.
 * Every shape is drawn here procedurally - no image assets - so there is nothing to license.
 *
 * Any scatter an effect needs (stars, sparks, a fighter swarm's spread) comes from a generator
 * seeded by the match or the event, never Math.random, so two devices watching the same shot draw
 * the same sparks.
 */

import { Application, Container, Graphics } from 'pixi.js';
import { courseAngle } from '../../../lib/geometry.ts';
import { normalizeFleetColor } from '../../../lib/normalize.ts';
import type { ActiveFx, PlaybackFrame } from '../../../lib/playback/playback.ts';
import type { MoveEvent } from '../../../lib/playback/timeline.ts';
import { applyViewport, markerScale, tableToContent, type ContentSize, type MapViewport } from '../../../lib/playback/viewTransform.ts';
import type { MatchSnapshot, TablePoint } from '../../../types.ts';

export type SceneInput = {
  snapshot: MatchSnapshot;
  moves: MoveEvent[];
  frame: PlaybackFrame;
  /** Ships the timeline will blow up later in the playback, still drawn whole until then. */
  pendingKills: Set<string>;
  focusedShipId?: string;
};

export class TableScene {
  readonly app = new Application();
  private readonly world = new Container();
  private readonly stars = new Graphics();
  private readonly trails = new Graphics();
  private readonly ships = new Graphics();
  private readonly fx = new Graphics();
  private size: ContentSize = { width: 1, height: 1 };
  private viewport: MapViewport = { scale: 1, x: 0, y: 0 };
  private seed = 1;
  private destroyed = false;
  private last: SceneInput | null = null;

  async init(host: HTMLElement, seedText: string): Promise<boolean> {
    await this.app.init({
      backgroundAlpha: 0,
      antialias: true,
      autoDensity: true,
      resolution: Math.min(2, window.devicePixelRatio || 1),
      width: Math.max(1, host.clientWidth),
      height: Math.max(1, host.clientHeight),
      preference: 'webgl',
    });
    if (this.destroyed) {
      this.app.destroy(true, { children: true });
      return false;
    }

    this.seed = hash(seedText);
    this.app.canvas.classList.add('pixi-map-canvas');
    host.appendChild(this.app.canvas);
    this.world.addChild(this.stars, this.trails, this.ships, this.fx);
    this.app.stage.addChild(this.world);
    // Rendered on demand: the map is still most of the time, and a ticker running at 60 Hz over an
    // idle table is a phone battery spent on nothing.
    this.app.ticker.stop();
    return true;
  }

  resize(size: ContentSize) {
    if (size.width === this.size.width && size.height === this.size.height) {
      return;
    }

    this.size = size;
    this.app.renderer.resize(Math.max(1, size.width), Math.max(1, size.height));
    this.drawStars();
    if (this.last) {
      this.draw(this.last);
    }
  }

  setViewport(viewport: MapViewport) {
    // A pan only moves the world. Geometry is drawn in content pixels with line widths divided by
    // the scale, so only a zoom has to rebuild it.
    const rescaled = viewport.scale !== this.viewport.scale;
    this.viewport = viewport;
    applyViewport(this.world, viewport);
    if (rescaled && this.last) {
      this.draw(this.last);
    } else {
      this.app.render();
    }
  }

  draw(input: SceneInput) {
    this.last = input;
    const px = markerScale(this.viewport);
    const table = { tableWidth: input.snapshot.tableWidth, tableDepth: input.snapshot.tableDepth };
    const at = (point: TablePoint) => tableToContent(point, table, this.size);

    this.trails.clear();
    for (const move of input.moves) {
      const focused = move.shipId === input.focusedShipId;
      const pose = input.frame.poses.get(move.shipId);
      const first = at(move.legs[0].from);
      this.trails.moveTo(first.x, first.y);
      for (const leg of move.legs) {
        const end = at(leg.to);
        this.trails.lineTo(end.x, end.y);
      }

      this.trails.stroke({ width: (focused ? 2 : 1.25) * px, color: 0xf5c766, alpha: focused ? 0.75 : 0.32 });
      if (pose && !input.frame.done) {
        // A soft wake behind the hull while it is still under way.
        const here = at(pose);
        this.trails.circle(here.x, here.y, 10 * px).fill({ color: 0xf5c766, alpha: 0.12 });
      }
    }

    this.ships.clear();
    for (const ship of input.snapshot.ships) {
      const fleet = input.snapshot.fleets.find((item) => item.id === ship.fleetId);
      const color = colorNumber(normalizeFleetColor(fleet?.fleetColor));
      const pose = input.frame.poses.get(ship.id);
      const point = at(pose ?? { x: ship.positionX, y: ship.positionY });
      const course = pose?.course ?? ship.currentCourse;
      const dead = ship.isDestroyed && !input.pendingKills.has(ship.id);
      drawHull(this.ships, point, course, px, dead ? 0x55606a : color, dead, ship.id === input.focusedShipId);
    }

    this.fx.clear();
    for (const active of input.frame.active) {
      this.drawFx(active, at, px);
    }

    this.app.render();
    // What was drawn, for the e2e check: a canvas cannot otherwise be asked what is on it.
    this.app.canvas.dataset.ships = String(input.snapshot.ships.length);
    this.app.canvas.dataset.effects = String(input.frame.active.length);
  }

  private drawStars() {
    const random = seeded(this.seed);
    const count = Math.round(Math.min(600, (this.size.width * this.size.height) / 2400));
    this.stars.clear();
    for (let index = 0; index < count; index += 1) {
      const radius = random() < 0.92 ? 0.6 + random() * 0.6 : 1.2 + random() * 0.8;
      this.stars.circle(random() * this.size.width, random() * this.size.height, radius).fill({ color: random() < 0.15 ? 0xf5c766 : 0xcfeaff, alpha: 0.25 + random() * 0.55 });
    }
  }

  private drawFx(active: ActiveFx, at: (point: TablePoint) => TablePoint, px: number) {
    const { step: fire, phase, progress } = active;
    const from = at(fire.event.from);
    const to = at(fire.event.to);
    const color = fire.spec.color;
    const random = seeded(this.seed ^ (fire.event.step * 7919));
    const g = this.fx;

    if (phase === 'travel') {
      switch (fire.spec.travel) {
        case 'beam': {
          const head = lerp(from, to, Math.min(1, progress * 1.8));
          const fade = progress > 0.7 ? 1 - (progress - 0.7) / 0.3 : 1;
          g.moveTo(from.x, from.y).lineTo(head.x, head.y).stroke({ width: 9 * px, color, alpha: 0.18 * fade });
          g.moveTo(from.x, from.y).lineTo(head.x, head.y).stroke({ width: 3.5 * px, color, alpha: 0.55 * fade });
          g.moveTo(from.x, from.y).lineTo(head.x, head.y).stroke({ width: 1.2 * px, color: 0xffffff, alpha: 0.95 * fade });
          g.circle(from.x, from.y, (9 - 6 * progress) * px).fill({ color: 0xffffff, alpha: 0.7 * fade });
          break;
        }
        case 'needle': {
          const flicker = (Math.floor(progress * 12) % 2) === 0 ? 1 : 0.45;
          g.moveTo(from.x, from.y).lineTo(to.x, to.y).stroke({ width: 1 * px, color, alpha: 0.9 * flicker });
          g.circle(to.x, to.y, 4 * px).fill({ color, alpha: 0.6 * flicker });
          break;
        }
        case 'projectile': {
          const eased = progress * progress;
          for (let trail = 8; trail >= 0; trail -= 1) {
            const point = lerp(from, to, Math.max(0, eased - trail * 0.025));
            g.circle(point.x, point.y, (trail === 0 ? 4 : 3 - trail * 0.25) * px).fill({ color: trail === 0 ? 0xffffff : color, alpha: trail === 0 ? 1 : 0.5 - trail * 0.05 });
          }
          break;
        }
        case 'swarm': {
          for (let dart = 0; dart < 5; dart += 1) {
            const lag = dart * 0.06;
            const t = Math.max(0, Math.min(1, (progress - lag) / (1 - lag)));
            const bend = (random() - 0.5) * 60 * px;
            const point = curve(from, to, t, bend);
            g.circle(point.x, point.y, 2.2 * px).fill({ color, alpha: 0.95 });
          }
          break;
        }
        case 'flash':
          g.circle(to.x, to.y, 6 * px * progress).fill({ color, alpha: 0.6 });
          break;
        case 'salvo': {
          // A cluster of missiles, each on its own slightly bowed line, closing together.
          for (let missile = 0; missile < 6; missile += 1) {
            const bend = (random() - 0.5) * 50 * px;
            const lag = random() * 0.15;
            const t = Math.max(0, Math.min(1, (progress - lag) / (1 - lag)));
            for (let trail = 4; trail >= 0; trail -= 1) {
              const point = curve(from, to, Math.max(0, t - trail * 0.03), bend);
              g.circle(point.x, point.y, (trail === 0 ? 2.6 : 1.8) * px).fill({ color: trail === 0 ? 0xffffff : color, alpha: trail === 0 ? 1 : 0.45 - trail * 0.08 });
            }
          }
          break;
        }
        case 'tracer': {
          // Short bright dashes streaming out from the defender towards what it is shooting at.
          for (let dash = 0; dash < 8; dash += 1) {
            const t = (progress * 1.6 + dash / 8) % 1;
            const a = lerp(from, to, t);
            const b = lerp(from, to, Math.min(1, t + 0.04));
            g.moveTo(a.x, a.y).lineTo(b.x, b.y).stroke({ width: 1.6 * px, color, alpha: 0.85 });
          }
          break;
        }
        case 'flight':
          // The group itself is moving (its pose comes from the playback); a faint line shows where to.
          g.moveTo(from.x, from.y).lineTo(to.x, to.y).stroke({ width: 1 * px, color, alpha: 0.35 * (1 - progress) });
          break;
        case 'none':
          break;
      }
      return;
    }

    const fade = 1 - progress;
    for (const impact of fire.impacts) {
      switch (impact) {
        case 'shimmer':
          g.circle(to.x, to.y, (18 + 10 * progress) * px).stroke({ width: 2 * px, color: 0x8fd3ff, alpha: 0.8 * fade });
          g.circle(to.x, to.y, (18 + 10 * progress) * px).fill({ color: 0x8fd3ff, alpha: 0.08 * fade });
          break;
        case 'sparks':
          for (let spark = 0; spark < 10; spark += 1) {
            const angle = random() * Math.PI * 2;
            const reach = (6 + random() * 18) * progress * px;
            g.moveTo(to.x, to.y).lineTo(to.x + Math.cos(angle) * reach, to.y + Math.sin(angle) * reach).stroke({ width: 1.4 * px, color: 0xffd27a, alpha: fade });
          }
          break;
        case 'explosion':
          g.circle(to.x, to.y, 42 * progress * px).fill({ color: 0xff7a2f, alpha: 0.55 * fade });
          g.circle(to.x, to.y, 22 * progress * px).fill({ color: 0xfff1c2, alpha: 0.8 * fade });
          g.circle(to.x, to.y, 56 * progress * px).stroke({ width: 2 * px, color: 0xffb347, alpha: 0.6 * fade });
          break;
        case 'miss':
          g.circle(to.x + (random() - 0.5) * 24 * px, to.y + (random() - 0.5) * 24 * px, 5 * px * (0.5 + progress)).fill({ color: 0x9aa6b2, alpha: 0.5 * fade });
          break;
        case 'intercept':
          // Small flashes short of the target, where the interceptions happened.
          for (let flash = 0; flash < 5; flash += 1) {
            const point = lerp(from, to, 0.7 + random() * 0.25);
            g.circle(point.x + (random() - 0.5) * 14 * px, point.y + (random() - 0.5) * 14 * px, (2 + 4 * progress) * px).fill({ color: 0x9fffd0, alpha: 0.7 * fade });
          }
          break;
        case 'flare':
          // A systems failure: an amber pulse around the hull.
          g.circle(to.x, to.y, (14 + 14 * progress) * px).stroke({ width: 3 * px, color: 0xffd27a, alpha: 0.9 * fade });
          g.circle(to.x, to.y, 10 * px).fill({ color: 0xffd27a, alpha: 0.25 * fade });
          break;
      }
    }
  }

  destroy() {
    this.destroyed = true;
    try {
      this.app.destroy(true, { children: true });
    } catch {
      // Not initialised yet; init sees the flag and tears itself down.
    }
  }
}

/** A placeholder hull: an arrowhead pointing along the course, the same size at every zoom. */
function drawHull(g: Graphics, at: TablePoint, course: number, px: number, color: number, dead: boolean, focused: boolean) {
  const angle = (courseAngle(course) * Math.PI) / 180;
  const rotate = (x: number, y: number) => ({
    x: at.x + (x * Math.cos(angle) - y * Math.sin(angle)) * px,
    y: at.y + (x * Math.sin(angle) + y * Math.cos(angle)) * px,
  });
  const points = [rotate(0, -13), rotate(9, 10), rotate(0, 5), rotate(-9, 10)];
  if (focused) {
    g.circle(at.x, at.y, 22 * px).stroke({ width: 1.5 * px, color: 0xf5c766, alpha: 0.85 });
  }

  g.circle(at.x, at.y, 16 * px).fill({ color, alpha: dead ? 0.05 : 0.12 });
  g.poly(points.flatMap((point) => [point.x, point.y])).fill({ color, alpha: dead ? 0.4 : 0.95 }).stroke({ width: 1 * px, color: 0x01090c, alpha: 0.8 });
  if (dead) {
    g.moveTo(at.x - 8 * px, at.y - 8 * px).lineTo(at.x + 8 * px, at.y + 8 * px).moveTo(at.x + 8 * px, at.y - 8 * px).lineTo(at.x - 8 * px, at.y + 8 * px).stroke({ width: 2 * px, color: 0xff6b6b, alpha: 0.8 });
  }
}

function lerp(from: TablePoint, to: TablePoint, amount: number): TablePoint {
  return { x: from.x + (to.x - from.x) * amount, y: from.y + (to.y - from.y) * amount };
}

/** A gentle arc from one point to another, bowed sideways by `bend` pixels at its middle. */
function curve(from: TablePoint, to: TablePoint, t: number, bend: number): TablePoint {
  const straight = lerp(from, to, t);
  const length = Math.hypot(to.x - from.x, to.y - from.y) || 1;
  const lift = Math.sin(Math.PI * t) * bend;
  return { x: straight.x + (-(to.y - from.y) / length) * lift, y: straight.y + ((to.x - from.x) / length) * lift };
}

function colorNumber(css: string): number {
  const hex = /^#([0-9a-f]{6})$/i.exec(css)?.[1];
  return hex ? parseInt(hex, 16) : 0x47f1ff;
}

function hash(text: string): number {
  let value = 2166136261;
  for (let index = 0; index < text.length; index += 1) {
    value = Math.imul(value ^ text.charCodeAt(index), 16777619);
  }

  return value >>> 0;
}

/** mulberry32: small, fast, and the same sequence everywhere for the same seed. */
function seeded(seed: number): () => number {
  let state = seed >>> 0;
  return () => {
    state = (state + 0x6d2b79f5) >>> 0;
    let t = state;
    t = Math.imul(t ^ (t >>> 15), t | 1);
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}
