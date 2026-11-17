/** The SDK's `language` option takes a BCP 47 tag; only the UI languages the app supports are mapped (default English). */
export const azureMapsLanguage = (uiLanguage: string) => (uiLanguage.toLowerCase().startsWith('pl') ? 'pl-PL' : 'en-US')
