// Runs before the stylesheet applies, so the loading screen shows in the theme of the last visit
// (see Preferences.ThemeKey). A file instead of an inline script, so the Content-Security-Policy
// can forbid inline scripts. The stored value is not trusted: only a plain word is used.
(function () {
    'use strict';
    var theme = 'classic';
    try {
        var stored = (localStorage.getItem('triominos.theme') || '').toLowerCase();
        if (/^[a-z]{1,20}$/.test(stored)) {
            theme = stored;
        }
    } catch (e) {
        // Storage switched off: the classic theme.
    }
    document.documentElement.className = 'js';
    document.documentElement.dataset.theme = theme;
})();
