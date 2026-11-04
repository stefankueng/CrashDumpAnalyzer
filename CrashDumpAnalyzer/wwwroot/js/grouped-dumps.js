(() => {
    const slider = document.getElementById('weeksSlider');
    const weeksValue = document.getElementById('weeksValue');
    slider.addEventListener('input', () => {
        weeksValue.textContent = slider.value;
    });
    slider.addEventListener('change', () => {
        window.location.href = '/Home/GroupedDumps?weeksBack=' + slider.value;
    });
})();
