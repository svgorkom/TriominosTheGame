// The Worker. Only /api/* and /ws/* reach it (see run_worker_first in wrangler.jsonc); the
// Blazor site itself is served as static files, which cost no Worker requests.

import type { Env } from './gameRoom';

export { GameRoom } from './gameRoom';

/** Consonants only, so a code never spells a word and never mixes up 0/O or 1/I. */
const ALPHABET = 'BCDFGHJKLMNPQRSTVWXZ';
const CODE_LENGTH = 5;
const CODE_PATTERN = new RegExp(`^[${ALPHABET}]{${CODE_LENGTH}}$`);

function newCode(): string {
  const bytes = crypto.getRandomValues(new Uint8Array(CODE_LENGTH));
  return Array.from(bytes, (b) => ALPHABET[b % ALPHABET.length]).join('');
}

function json(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), { status, headers: { 'content-type': 'application/json' } });
}

export default {
  async fetch(request: Request, env: Env): Promise<Response> {
    const url = new URL(request.url);

    if (url.pathname === '/api/rooms' && request.method === 'POST') {
      // A code that is taken (rare, there are 3.2 million) is replaced by another.
      for (let attempt = 0; attempt < 5; attempt++) {
        const code = newCode();
        const room = env.ROOMS.get(env.ROOMS.idFromName(code));
        const created = await room.fetch('https://room/create', { method: 'POST', headers: { 'x-room-code': code } });
        if (created.status === 201) {
          return json({ code }, 201);
        }
      }

      return json({ error: 'no_code' }, 503);
    }

    const socket = /^\/ws\/([A-Za-z]+)$/.exec(url.pathname);
    if (socket) {
      const code = socket[1].toUpperCase();
      if (!CODE_PATTERN.test(code)) {
        return json({ error: 'room_not_found' }, 404);
      }

      return env.ROOMS.get(env.ROOMS.idFromName(code)).fetch(request);
    }

    // Not for the Worker: let the static files answer.
    return env.ASSETS.fetch(request);
  },
} satisfies ExportedHandler<Env>;
