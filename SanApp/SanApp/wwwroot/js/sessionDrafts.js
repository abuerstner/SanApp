(() => {
    const proofKey = "sanapp.sessionDraft.deleteProof";
    const attemptKey = "sanapp.sessionDraft.loginAttempt";

    const saveTabProof = (draftId, capability) => {
        if (typeof draftId !== "string" || typeof capability !== "string" || capability.length < 40) {
            throw new Error("Invalid session draft proof.");
        }
        sessionStorage.removeItem(attemptKey);
        sessionStorage.setItem(proofKey, JSON.stringify({ draftId, capability }));
    };

    const readTabProof = () => {
        const value = sessionStorage.getItem(proofKey);
        if (value === null) {
            return null;
        }
        const proof = JSON.parse(value);
        if (!proof || typeof proof.draftId !== "string" ||
            !/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(proof.draftId) ||
            typeof proof.capability !== "string" || proof.capability.length < 40) {
            throw new Error("Invalid session draft proof.");
        }
        return proof;
    };

    const clearTabProof = () => {
        sessionStorage.removeItem(proofKey);
        sessionStorage.removeItem(attemptKey);
    };

    if (document.querySelector("[data-login-attempt-reset]")) {
        sessionStorage.removeItem(attemptKey);
    }

    document.addEventListener("submit", event => {
        const form = event.target;
        if (!(form instanceof HTMLFormElement) || !form.matches("[data-session-draft-switch]")) {
            return;
        }

        try {
            const proof = readTabProof();
            if (proof) {
                const input = form.elements.namedItem("loginAttempt");
                const saved = sessionStorage.getItem(attemptKey);
                const attempt = saved === null ? null : JSON.parse(saved);
                if (saved !== null && (!attempt || attempt.draftId !== proof.draftId ||
                    typeof attempt.token !== "string" || !attempt.token)) {
                    throw new Error("Invalid login attempt.");
                }
                if (attempt) {
                    input.value = attempt.token;
                } else {
                    sessionStorage.setItem(attemptKey, JSON.stringify({ draftId: proof.draftId, token: input.value }));
                }
                form.elements.namedItem("draftId").value = proof.draftId;
                form.elements.namedItem("draftDeleteCapability").value = proof.capability;
            }
        } catch (error) {
            event.preventDefault();
            let alert = form.querySelector("[data-draft-proof-error]");
            if (!alert) {
                alert = document.createElement("p");
                alert.setAttribute("role", "alert");
                alert.setAttribute("data-draft-proof-error", "");
                form.prepend(alert);
            }
            alert.textContent = "Der Entwurfsnachweis konnte nicht geprüft werden. Die Anmeldung wurde nicht durchgeführt. Bitte laden Sie die Seite erneut.";
        }
    });

    window.sanAppDrafts = { saveTabProof, readTabProof, clearTabProof };

    const completion = document.querySelector("[data-session-switch-complete]");
    if (completion instanceof HTMLElement) {
        const status = completion.querySelector("[data-switch-status]");
        const retry = completion.querySelector("[data-switch-retry]");
        let pending = false;
        const confirmSwitch = async () => {
            if (pending) {
                return;
            }
            pending = true;
            retry.hidden = true;
            try {
                const proof = readTabProof();
                const attempt = JSON.parse(sessionStorage.getItem(attemptKey));
                if (!proof || !attempt || attempt.draftId !== proof.draftId || typeof attempt.token !== "string") {
                    throw new Error("Missing switch proof.");
                }
                const tokenResponse = await fetch("/api/antiforgery", { credentials: "same-origin", cache: "no-store" });
                if (!tokenResponse.ok) {
                    throw new Error("Antiforgery token unavailable.");
                }
                const token = await tokenResponse.json();
                const form = new URLSearchParams({
                    __RequestVerificationToken: token.requestToken,
                    draftId: proof.draftId,
                    draftDeleteCapability: proof.capability,
                    loginAttempt: attempt.token
                });
                const response = await fetch("/account/switch-confirmed", {
                    method: "POST",
                    credentials: "same-origin",
                    cache: "no-store",
                    body: form
                });
                if (!response.ok) {
                    throw new Error("Switch confirmation unavailable.");
                }
                clearTabProof();
                window.location.replace(completion.dataset.returnUrl || "/");
            } catch (error) {
                status.setAttribute("role", "alert");
                status.textContent = "Der Kontowechsel wurde angemeldet, aber sein Abschluss konnte nicht bestätigt werden. Bitte prüfen Sie ihn erneut.";
                retry.hidden = false;
            } finally {
                pending = false;
            }
        };
        retry.addEventListener("click", confirmSwitch);
        confirmSwitch();
    }
})();
