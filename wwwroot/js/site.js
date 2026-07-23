/**
 * @param {string} selector
 * @param {boolean} online
 */
function updateComponent(selector, online) {
    document
        .querySelectorAll(selector)
        .forEach(element => {
            if (!(element instanceof HTMLElement)) {
                return;
            }

            element.textContent = online ? "Online" : "Offline";
            element.dataset.online = online ? "true" : "false";
        });
}

/**
 * @param {string | undefined} value
 */
function updateStatusCheckedTime(value) {
    const checkedAt = value ? new Date(value) : null;
    const validDate = checkedAt && !Number.isNaN(checkedAt.getTime());

    document
        .querySelectorAll("[data-status-checked]")
        .forEach(element => {
            if (!validDate || !checkedAt) {
                element.textContent = "unavailable";
                return;
            }

            element.textContent = new Intl.DateTimeFormat(undefined, {
                hour: "numeric",
                minute: "2-digit",
                second: "2-digit"
            }).format(checkedAt);

            element.title = checkedAt.toLocaleString();

            if (element instanceof HTMLTimeElement) {
                element.dateTime = checkedAt.toISOString();
            }
        });
}

(() => {
    const body = document.body;
    const statusClasses = [
        "server-status-loading",
        "server-online",
        "server-offline"
    ];

    async function refreshServerStatus() {
        try {
            const response = await fetch("/api/server-status", {
                method: "GET",
                headers: {
                    Accept: "application/json"
                },
                cache: "no-store"
            });

            if (!response.ok) {
                throw new Error(`Status request failed: ${response.status}`);
            }

            const status = await response.json();

            body.classList.remove(...statusClasses);
            body.classList.add(status.online ? "server-online" : "server-offline");

            document
                .querySelectorAll("[data-server-status-text]")
                .forEach(element => {
                    element.textContent = status.online ? "Online" : "Offline";
                });

            document
                .querySelectorAll("[data-player-count]")
                .forEach(element => {
                    element.textContent = String(status.onlinePlayers);
                });

            updateComponent("[data-login-server-status]", status.loginServerOnline);
            updateComponent("[data-char-server-status]", status.charServerOnline);
            updateComponent("[data-map-server-status]", status.mapServerOnline);
            updateStatusCheckedTime(status.checkedAtUtc);
        } catch (error) {
            console.error("Unable to retrieve MaximoRO server status.", error);

            body.classList.remove(...statusClasses);
            body.classList.add("server-offline");

            document
                .querySelectorAll("[data-server-status-text]")
                .forEach(element => {
                    element.textContent = "Offline";
                });

            updateComponent("[data-login-server-status]", false);
            updateComponent("[data-char-server-status]", false);
            updateComponent("[data-map-server-status]", false);
            updateStatusCheckedTime(undefined);
        }
    }

    refreshServerStatus();
    window.setInterval(refreshServerStatus, 10000);
})();

(() => {
    const normalize = value => value.trim().toLocaleLowerCase();

    document
        .querySelectorAll("[data-collection-root]")
        .forEach(root => {
            const input = root.querySelector("[data-collection-search]");
            const buttons = Array.from(
                root.querySelectorAll("[data-collection-filter]")
            );
            const items = Array.from(
                root.querySelectorAll("[data-collection-item]")
            );
            const count = root.querySelector("[data-collection-count]");
            const empty = root.querySelector("[data-collection-empty]");
            const label = root.classList.contains("news-page-section")
                ? "updates"
                : "guides";

            if (!(input instanceof HTMLInputElement) || items.length === 0) {
                return;
            }

            let activeFilter = "all";

            function refreshCollection() {
                const query = normalize(input.value);
                let visibleCount = 0;

                items.forEach(item => {
                    if (!(item instanceof HTMLElement)) {
                        return;
                    }

                    const category = item.dataset.collectionCategory ?? "";
                    const matchesFilter =
                        activeFilter === "all" || category === activeFilter;
                    const matchesSearch =
                        query.length === 0 || normalize(item.textContent ?? "").includes(query);
                    const visible = matchesFilter && matchesSearch;

                    item.hidden = !visible;

                    if (visible) {
                        visibleCount += 1;
                    }
                });

                if (count) {
                    count.textContent = `${visibleCount} ${label}`;
                }

                if (empty instanceof HTMLElement) {
                    empty.hidden = visibleCount !== 0;
                }
            }

            input.addEventListener("input", refreshCollection);
            input.addEventListener("keydown", event => {
                if (event.key === "Escape" && input.value.length > 0) {
                    input.value = "";
                    refreshCollection();
                }
            });

            buttons.forEach(button => {
                button.addEventListener("click", () => {
                    activeFilter = button.getAttribute("data-collection-filter") ?? "all";

                    buttons.forEach(candidate => {
                        const selected = candidate === button;
                        candidate.classList.toggle("active", selected);
                        candidate.setAttribute("aria-pressed", String(selected));
                    });

                    refreshCollection();
                });
            });

            refreshCollection();
        });
})();