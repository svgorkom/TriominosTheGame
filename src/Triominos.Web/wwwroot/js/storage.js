// Browser storage for the settings and the last players. Reading or writing can fail (storage
// switched off, a private window, a full disk); the game then simply starts from its defaults.

export function get(key) {
  try {
    return localStorage.getItem(key);
  } catch {
    return null;
  }
}

export function set(key, value) {
  try {
    localStorage.setItem(key, value);
  } catch {
    // Not stored; nothing else depends on it.
  }
}
