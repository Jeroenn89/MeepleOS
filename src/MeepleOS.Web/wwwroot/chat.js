const observedChatWindows = new WeakSet();
const boundComposers = new WeakSet();

window.meepleChat = {
    scrollToBottom(element) {
        element.scrollTop = element.scrollHeight;
    },
    animateNewMessages(element) {
        const firstVisit = !observedChatWindows.has(element);
        observedChatWindows.add(element);
        const reducedMotion = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
        for (const message of element.querySelectorAll(".chat-message:not([data-written])")) {
            message.dataset.written = "true";
            if (firstVisit || reducedMotion) continue;

            const length = message.querySelector(".chat-message-text")?.textContent?.length ?? 0;
            const duration = Math.min(1600, Math.max(550, length * 18));
            message.style.setProperty("--write-duration", `${duration}ms`);
            message.classList.add("chat-writing");
            window.setTimeout(() => message.classList.remove("chat-writing"), duration);
        }
    },
    bindComposer(textarea) {
        if (boundComposers.has(textarea)) return;
        boundComposers.add(textarea);
        const paper = textarea.closest(".chat-compose-paper");
        if (!paper) return;
        let timer;
        textarea.addEventListener("input", () => {
            paper.classList.remove("is-writing");
            void paper.offsetWidth;
            paper.classList.add("is-writing");
            window.clearTimeout(timer);
            timer = window.setTimeout(() => paper.classList.remove("is-writing"), 500);
        });
        textarea.addEventListener("keydown", event => {
            if (event.key !== "Enter" || event.shiftKey || event.ctrlKey || event.altKey || event.metaKey || event.isComposing) return;
            event.preventDefault();
            textarea.dispatchEvent(new Event("input", { bubbles: true }));
            textarea.closest(".chat-form")?.querySelector("button")?.click();
        });
    }
};
