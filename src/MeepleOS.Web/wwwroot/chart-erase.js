const activeCharts = new WeakMap();

export function attach(chart) {
    detach(chart);

    const plot = chart.querySelector(".line-chart-plot");
    const line = plot?.querySelector(".chart-main-line");
    const pencil = plot?.querySelector(".chart-pencil-anchor");
    const clip = plot?.querySelector(".chart-erase-clip");
    const eraser = plot?.querySelector(".chart-eraser-anchor");
    if (!plot || !line || !pencil || !clip || !eraser) return;

    const reducedMotion = window.matchMedia("(prefers-reduced-motion: reduce)");
    const lineLength = line.getTotalLength();
    const drawDuration = 1800;
    let drawFrame = 0;
    let frame = 0;

    function placePencil(point) {
        pencil.style.left = `${point.x / 10}%`;
        pencil.style.top = `${point.y / 3.2}%`;
    }

    function finishDrawing() {
        if (drawFrame) cancelAnimationFrame(drawFrame);
        drawFrame = 0;
        line.style.strokeDasharray = "";
        line.style.strokeDashoffset = "";
        placePencil(line.getPointAtLength(line.getTotalLength()));
    }

    if (!reducedMotion.matches && lineLength > 0) {
        line.style.strokeDasharray = `${lineLength} ${lineLength}`;
        line.style.strokeDashoffset = `${lineLength}`;
        placePencil(line.getPointAtLength(0));
        const startedAt = performance.now();
        function draw(now) {
            const progress = Math.min(1, (now - startedAt) / drawDuration);
            const eased = 1 - (1 - progress) ** 3;
            const distance = eased * lineLength;
            line.style.strokeDashoffset = `${lineLength - distance}`;
            placePencil(line.getPointAtLength(distance));
            if (progress < 1 && !reducedMotion.matches) drawFrame = requestAnimationFrame(draw);
            else finishDrawing();
        }
        drawFrame = requestAnimationFrame(draw);
    }

    function update() {
        frame = 0;
        if (reducedMotion.matches) {
            if (drawFrame) finishDrawing();
            clip.setAttribute("x", "0");
            clip.setAttribute("width", "1000");
            eraser.style.opacity = "0";
            return;
        }

        const chartTop = chart.getBoundingClientRect().top + window.scrollY;
        const start = Math.max(0, chartTop - window.innerHeight * 0.55);
        const end = Math.max(start + 200, chartTop + chart.offsetHeight - window.innerHeight * 0.18);
        const progress = Math.max(0, Math.min(1, (window.scrollY - start) / (end - start)));
        const x = 48 + progress * 918;
        const edge = progress === 0 ? 0 : progress === 1 ? 1000 : x;

        clip.setAttribute("x", edge.toFixed(2));
        clip.setAttribute("width", (1000 - edge).toFixed(2));
        eraser.style.opacity = progress > 0 && progress < 1 ? "1" : "0";
        if (progress <= 0 || progress >= 1) return;

        // The chart path runs from left to right, so its point at x can be found by bisection.
        let low = 0;
        let high = line.getTotalLength();
        for (let i = 0; i < 14; i++) {
            const middle = (low + high) / 2;
            if (line.getPointAtLength(middle).x < x) low = middle;
            else high = middle;
        }
        const point = line.getPointAtLength((low + high) / 2);
        eraser.style.left = `${x / 10}%`;
        eraser.style.top = `${point.y / 3.2}%`;
    }

    function schedule() {
        if (!frame) frame = requestAnimationFrame(update);
    }

    window.addEventListener("scroll", schedule, { passive: true });
    window.addEventListener("resize", schedule);
    reducedMotion.addEventListener("change", schedule);
    const resizeObserver = new ResizeObserver(schedule);
    resizeObserver.observe(chart);
    activeCharts.set(chart, () => {
        window.removeEventListener("scroll", schedule);
        window.removeEventListener("resize", schedule);
        reducedMotion.removeEventListener("change", schedule);
        resizeObserver.disconnect();
        if (drawFrame) cancelAnimationFrame(drawFrame);
        if (frame) cancelAnimationFrame(frame);
    });
    schedule();
}

export function detach(chart) {
    activeCharts.get(chart)?.();
    activeCharts.delete(chart);
}
