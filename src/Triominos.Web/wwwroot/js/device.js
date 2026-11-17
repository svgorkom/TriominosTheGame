// What kind of screen the game runs on, for the defaults of a first visit.

/** True when the main pointer is a finger (a tablet or a phone), not a mouse or a trackpad. */
export function isTouchScreen() {
  try {
    return window.matchMedia('(pointer: coarse)').matches;
  } catch {
    return false;
  }
}
