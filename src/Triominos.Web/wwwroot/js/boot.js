// Starts Blazor by hand so a failed start can be shown on the loading screen. Blazor replaces the
// content of #app when it has started, which removes the loading screen; the screen is only
// touched here while it is still there. The progress bar needs no script: Blazor sets
// --blazor-load-percentage on the page while it downloads and the CSS draws it.
(function () {
    'use strict';

    var SLOW_AFTER_MS = 20000;

    function loadingScreen() {
        return document.querySelector('#app .loading');
    }

    function fail(title, text) {
        var screen = loadingScreen();
        if (!screen) {
            return;
        }

        clearInterval(watch);
        screen.classList.add('failed');
        screen.setAttribute('role', 'alert');
        var card = screen.querySelector('.loading-card');
        var progress = card.querySelector('.loading-progress');
        if (progress) {
            progress.remove();
        }

        var problem = document.createElement('div');
        problem.className = 'loading-problem';
        var heading = document.createElement('h2');
        heading.textContent = title;
        var paragraph = document.createElement('p');
        paragraph.textContent = text;
        var button = document.createElement('button');
        button.type = 'button';
        button.textContent = 'Opnieuw proberen';
        button.addEventListener('click', function () {
            location.reload();
        });
        problem.append(heading, paragraph, button);
        card.append(problem);
    }

    // The slow-loading hint: shown when the percentage has not moved for a while.
    var lastPercentage = '';
    var lastChange = Date.now();
    var watch = setInterval(function () {
        var screen = loadingScreen();
        if (!screen) {
            clearInterval(watch);
            return;
        }

        var now = getComputedStyle(document.documentElement).getPropertyValue('--blazor-load-percentage');
        if (now !== lastPercentage) {
            lastPercentage = now;
            lastChange = Date.now();
            screen.querySelector('.loading-slow').hidden = true;
        } else if (Date.now() - lastChange > SLOW_AFTER_MS) {
            screen.querySelector('.loading-slow').hidden = false;
        }
    }, 1000);

    if (typeof WebAssembly === 'undefined') {
        fail('Deze browser kan Triominos niet starten', 'Triominos heeft WebAssembly nodig. Gebruik een recente versie van Chrome, Edge, Firefox of Safari.');
        return;
    }

    if (!window.Blazor) {
        fail('Het laden is mislukt', 'Triominos kon niet worden geladen. Controleer je verbinding en probeer het opnieuw.');
        return;
    }

    window.Blazor.start().catch(function () {
        fail('Het laden is mislukt', 'Triominos kon niet worden geladen. Controleer je verbinding en probeer het opnieuw.');
    });
})();
