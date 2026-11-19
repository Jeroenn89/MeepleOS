window.meepleHeaderScroll = (() => {
    let activeHeader;
    let scrollHandler;
    let resizeHandler;
    let headerObserver;

    function start() {
        const header = document.querySelector(".site-head");
        if (!header || header === activeHeader) return;

        if (scrollHandler) {
            window.removeEventListener("scroll", scrollHandler);
            document.removeEventListener("scroll", scrollHandler, true);
            window.removeEventListener("resize", resizeHandler);
            headerObserver?.disconnect();
        }

        activeHeader = header;
        const spacer = document.querySelector(".site-head-spacer");
        const syncHeaderHeight = () => {
            if (spacer) spacer.style.height = `${header.offsetHeight}px`;
        };
        resizeHandler = syncHeaderHeight;
        syncHeaderHeight();
        window.addEventListener("resize", resizeHandler);
        if ("ResizeObserver" in window) {
            headerObserver = new ResizeObserver(syncHeaderHeight);
            headerObserver.observe(header);
        }

        const getScrollY = () => Math.max(0, window.scrollY,
            document.documentElement.scrollTop, document.body.scrollTop);
        let previousScrollY = getScrollY();
        let downwardDistance = 0;
        let upwardDistance = 0;

        scrollHandler = () => {
            const scrollY = getScrollY();
            const movement = scrollY - previousScrollY;
            previousScrollY = scrollY;

            if (scrollY < header.offsetHeight || header.querySelector(".mobile-menu.is-open")) {
                header.classList.remove("is-scroll-hidden");
                downwardDistance = 0;
                upwardDistance = 0;
            } else if (movement > 0) {
                downwardDistance += movement;
                upwardDistance = 0;
                if (downwardDistance >= 14) header.classList.add("is-scroll-hidden");
            } else if (movement < 0) {
                upwardDistance -= movement;
                downwardDistance = 0;
                if (upwardDistance >= 32) header.classList.remove("is-scroll-hidden");
            }
        };

        window.addEventListener("scroll", scrollHandler, { passive: true });
        document.addEventListener("scroll", scrollHandler, true);
        header.addEventListener("click", () => header.classList.remove("is-scroll-hidden"));
    }

    return { start };
})();
