# UI and i18n Rules

- Persistent user-facing text in XAML must use `DynamicResource` so it updates when the app language changes.
- Do not store persistent display strings from `GetTranslation()` in ViewModel properties; use XAML triggers and localized resource keys instead.
- Transient messages such as snackbars or momentary inline validation errors may use `GetTranslation()` at the action point.
- Frontend layout must be responsive and visually stable. Preserve consistent spacing, avoid layout jumps between equivalent modes, and use controls/icons that match user expectations.
