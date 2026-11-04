(() => {
    const theme = localStorage.getItem('theme');
    if (theme === 'light' || theme === 'dark') {
        document.documentElement.setAttribute('data-bs-theme', theme);
    }
})();
