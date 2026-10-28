export async function login(form, baseUri) {
    const base = new URL(baseUri);
    if (base.origin !== window.location.origin) {
        throw new Error("The login endpoint must be on this LANCommander server.");
    }
    if (base.protocol !== "https:" &&
        !["localhost", "127.0.0.1", "[::1]"].includes(base.hostname)) {
        throw new Error("Use HTTPS before sending your LANCommander password.");
    }

    const username = form.elements.namedItem("username");
    const password = form.elements.namedItem("password");
    if (!username.value.trim() || !password.value) {
        throw new Error("Enter your LANCommander username and password.");
    }

    let response;
    try {
        response = await fetch(new URL("api/Auth/Login", base), {
            method: "POST",
            credentials: "same-origin",
            cache: "no-store",
            redirect: "error",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ username: username.value, password: password.value })
        });
    } finally {
        password.value = "";
    }

    if (!response.ok) {
        throw new Error("Login failed. Check your credentials and that password login is enabled on the LC server.");
    }
    const session = await response.json();
    if (typeof session.accessToken !== "string" || !session.accessToken ||
        typeof session.expiration !== "string" || Number.isNaN(Date.parse(session.expiration))) {
        throw new Error("The LC login response did not contain a valid access token and expiry.");
    }

    // Do not return or retain the refresh token: this page only helps configure a client.
    return { accessToken: session.accessToken, expiration: session.expiration };
}

export async function copy(value) {
    await navigator.clipboard.writeText(value);
}
