// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// Write your JavaScript code.
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

            element.textContent =
                online
                    ? "Online"
                    : "Offline";

            element.dataset.online =
                online
                    ? "true"
                    : "false";
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
                throw new Error(
                    `Status request failed: ${response.status}`
                );
            }

            const status = await response.json();

            body.classList.remove(...statusClasses);
            body.classList.add(
                status.online
                    ? "server-online"
                    : "server-offline"
            );

            document
                .querySelectorAll("[data-server-status-text]")
                .forEach(element => {
                    element.textContent =
                        status.online
                            ? "Online"
                            : "Offline";
                });

            document
                .querySelectorAll("[data-player-count]")
                .forEach(element => {
                    element.textContent =
                        String(status.onlinePlayers);
                });

            updateComponent(
                "[data-login-server-status]",
                status.loginServerOnline
            );

            updateComponent(
                "[data-char-server-status]",
                status.charServerOnline
            );

            updateComponent(
                "[data-map-server-status]",
                status.mapServerOnline
            );
        } catch (error) {
            console.error(
                "Unable to retrieve MaximoRO server status.",
                error
            );

            body.classList.remove(...statusClasses);
            body.classList.add("server-offline");

            document
                .querySelectorAll("[data-server-status-text]")
                .forEach(element => {
                    element.textContent = "Offline";
                });
        }
    }


    refreshServerStatus();

    window.setInterval(
        refreshServerStatus,
        10000
    );
})();