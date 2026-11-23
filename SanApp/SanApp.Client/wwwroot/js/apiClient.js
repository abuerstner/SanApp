window.sanAppApi = {
    async send(method, url, body) {
        const headers = new Headers({ Accept: "application/json, application/problem+json" });
        let payload = body;
        if (body !== null) {
            headers.set("Content-Type", "application/json");
        }

        if (["POST", "PUT", "PATCH", "DELETE"].includes(method.toUpperCase())) {
            const tokenResponse = await fetch(new URL("api/antiforgery", document.baseURI), {
                method: "GET",
                credentials: "same-origin",
                cache: "no-store",
                headers: { Accept: "application/json" }
            });
            if (!tokenResponse.ok) {
                return await readResponse(tokenResponse);
            }

            const tokens = await tokenResponse.json();
            headers.set("X-CSRF-TOKEN", tokens.requestToken);
        }

        const response = await fetch(new URL(url, document.baseURI), {
            method,
            credentials: "same-origin",
            cache: "no-store",
            headers,
            body: payload
        });
        return await readResponse(response);
    }
};

async function readResponse(response) {
    return {
        statusCode: response.status,
        contentType: response.headers.get("content-type"),
        body: await response.text()
    };
}
