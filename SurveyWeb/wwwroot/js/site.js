(function () {
    const storageKey = 'surveyweb-theme';
    const classLight = 'theme-light';
    const classDark = 'theme-dark';

    function getIcon() {
        return document.getElementById('themeIcon');
    }

    function setIcon(theme) {
        const icon = getIcon();
        if (!icon) return;
        icon.classList.remove('bi-sun', 'bi-moon-stars');
        icon.classList.add(theme === classDark ? 'bi-sun' : 'bi-moon-stars');
    }

    function applyTheme(theme) {
        const root = document.documentElement;
        const body = document.body;
        const normalized = theme === classDark || theme === 'dark' ? classDark : classLight;

        body.classList.remove(classLight, classDark);
        body.classList.add(normalized);

        root.setAttribute('data-theme', normalized === classDark ? 'dark' : 'light');
        localStorage.setItem(storageKey, normalized);
        setIcon(normalized);
    }

    function toggleTheme() {
        const current = document.body.classList.contains(classDark) ? classDark : classLight;
        applyTheme(current === classDark ? classLight : classDark);
    }

    function bindToggle() {
        const toggle = document.getElementById('themeToggle');
        if (!toggle || toggle.dataset.bound === 'true') return;
        toggle.addEventListener('click', toggleTheme);
        toggle.dataset.bound = 'true';
    }

    function init() {
        const stored = localStorage.getItem(storageKey);
        if (stored === classLight || stored === classDark) {
            applyTheme(stored);
        } else {
            const prefersDark = window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches;
            applyTheme(prefersDark ? classDark : classLight);
        }
        bindToggle();
    }

    window.SurveyWebTheme = {
        apply: applyTheme,
        toggle: toggleTheme,
        bindToggle,
        init
    };

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init, { once: true });
    } else {
        init();
    }
})();
