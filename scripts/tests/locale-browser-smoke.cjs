'use strict'

const assert = require('node:assert/strict')
const fs = require('node:fs')
const http = require('node:http')
const { chromium } = require('patchright')
const { resolveAccountLocale } = require('../../dist/util/Locale.js')

async function runCase(langCode, geoLocale, expectedLocale, resolvedCountry) {
    const accountLocale = resolveAccountLocale({ langCode, geoLocale }, resolvedCountry)
    assert.equal(accountLocale.locale, expectedLocale)
    console.log(`LOCALE_CASE_PASS ${langCode}/${geoLocale} -> ${accountLocale.locale}`)

    let observedAcceptLanguage = ''
    const server = http.createServer((req, res) => {
        observedAcceptLanguage = String(req.headers['accept-language'] ?? '')
        res.writeHead(200, { 'content-type': 'text/html; charset=utf-8' })
        res.end('<!doctype html><html><body>locale smoke</body></html>')
    })

    await new Promise((resolve, reject) => {
        server.once('error', reject)
        server.listen(0, '127.0.0.1', resolve)
    })

    const address = server.address()
    assert.ok(address && typeof address === 'object')
    const url = `http://127.0.0.1:${address.port}/probe`

    const browser = await chromium.launch({
        headless: true,
        args: ['--lang=' + accountLocale.locale]
    })

    try {
        const context = await browser.newContext({
            locale: accountLocale.locale,
            extraHTTPHeaders: {
                'Accept-Language': accountLocale.acceptLanguage
            }
        })
        try {
            const page = await context.newPage()
            await page.goto(url, { waitUntil: 'domcontentloaded' })

            const navigatorLocale = await page.evaluate(() => navigator.language)
            assert.equal(navigatorLocale, expectedLocale)
            const preferredLocale = accountLocale.acceptedLocales[0].toLowerCase()
            assert.ok(observedAcceptLanguage.toLowerCase().startsWith(preferredLocale))
        } finally {
            await context.close()
        }
    } finally {
        await browser.close()
        await new Promise(resolve => server.close(resolve))
    }
}

async function main() {
    const source = fs.readFileSync('src/browser/Browser.ts', 'utf8')

    assert.match(source, /locale: uiLocale/)
    assert.match(source, /'Accept-Language': uiAcceptLanguage/)
    assert.match(source, /'--lang=' \+ uiLocale/)
    assert.doesNotMatch(source, /--lang=en-US/)

    await runCase('en', 'auto', 'en-US', 'US')
    await runCase('en-GB', 'auto', 'en-GB', undefined)

    console.log('LOCALE_BROWSER_SMOKE_PASS en-US')
    console.log('LOCALE_BROWSER_SMOKE_PASS en-GB')
}

main().catch(error => {
    console.error(error)
    process.exitCode = 1
})
