(() => {
    const slider = document.getElementById('weeksSlider');
    const weeksValue = document.getElementById('weeksValue');
    slider.addEventListener('input', () => {
        weeksValue.textContent = slider.value;
    });
    slider.addEventListener('change', () => {
        const parsedWeeksBack = Number.parseInt(slider.value, 10);
        const weeksBack = Number.isNaN(parsedWeeksBack) ? 0 : parsedWeeksBack;
        const query = new URLSearchParams({ weeksBack: String(weeksBack) }).toString();
        window.location.href = '/Home/GroupedDumps?' + query;
    });
})();
