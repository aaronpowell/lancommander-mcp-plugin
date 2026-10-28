import { test } from "node:test";
import assert from "node:assert/strict";
import { login, copy } from "../src/LANCommander.Mcp/Pages/setup.mjs";

function form() {
    const fields = { username: { value: "admin" }, password: { value: "secret" } };
    return { fields, elements: { namedItem: name => fields[name] } };
}

globalThis.window = { location: { origin: "https://lc.example" } };

test("login posts only to LC, clears password, and discards refresh token", async () => {
    const input = form();
    globalThis.fetch = async (url, options) => {
        assert.equal(url.href, "https://lc.example/lc/api/Auth/Login");
        assert.equal(options.method, "POST");
        assert.equal(options.credentials, "same-origin");
        assert.equal(options.cache, "no-store");
        assert.equal(options.redirect, "error");
        assert.deepEqual(JSON.parse(options.body), { username: "admin", password: "secret" });
        return {
            ok: true,
            json: async () => ({ accessToken: "token", refreshToken: "never-return-this", expiration: "2026-10-29T00:00:00Z" })
        };
    };
    assert.deepEqual(await login(input, "https://lc.example/lc/"), {
        accessToken: "token", expiration: "2026-10-29T00:00:00Z"
    });
    assert.equal(input.fields.password.value, "");
});

test("failed login clears password and reports an error", async () => {
    const input = form();
    globalThis.fetch = async () => ({ ok: false });
    await assert.rejects(login(input, "https://lc.example/"), /Login failed/);
    assert.equal(input.fields.password.value, "");
});

test("network failure clears password", async () => {
    const input = form();
    globalThis.fetch = async () => { throw new Error("offline"); };
    await assert.rejects(login(input, "https://lc.example/"), /offline/);
    assert.equal(input.fields.password.value, "");
});

test("empty credentials never reach the login endpoint", async () => {
    const input = form();
    input.fields.password.value = "";
    globalThis.fetch = async () => assert.fail("Must not send empty credentials");
    await assert.rejects(login(input, "https://lc.example/"), /Enter your/);
});

test("invalid session response is not accepted", async () => {
    for (const session of [{}, { accessToken: "token", expiration: "not-a-date" }]) {
        const input = form();
        globalThis.fetch = async () => ({ ok: true, json: async () => session });
        await assert.rejects(login(input, "https://lc.example/"), /valid access token/);
        assert.equal(input.fields.password.value, "");
    }
});

test("cross-origin destination is rejected before sending credentials", async () => {
    globalThis.fetch = async () => assert.fail("Must not send to another origin");
    await assert.rejects(login(form(), "https://attacker.example/"), /must be on this/);
});

test("insecure remote destination is rejected before sending credentials", async () => {
    window.location.origin = "http://lc.example";
    try {
        globalThis.fetch = async () => assert.fail("Must not send password over HTTP");
        await assert.rejects(login(form(), "http://lc.example/"), /Use HTTPS/);
    } finally {
        window.location.origin = "https://lc.example";
    }
});

test("copy writes exact value and propagates clipboard failures", async () => {
    let copied;
    Object.defineProperty(globalThis, "navigator", {
        configurable: true, value: { clipboard: { writeText: async value => { copied = value; } } }
    });
    await copy("token");
    assert.equal(copied, "token");
    navigator.clipboard.writeText = async () => { throw new Error("denied"); };
    await assert.rejects(copy("token"), /denied/);
});
