; The UI analyzer's rules (plan_client_ui §5.8.5a; docs/ui/authoring.md lists
; them with their fixes). RS2008 keeps this list honest: a rule added, changed
; or dropped has to be edited here on purpose.

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
SSUI001 | SourceSharp.Ui | Warning | Allocation in a describe method
SSUI002 | SourceSharp.Ui | Warning | A raw value where a token belongs
SSUI003 | SourceSharp.Ui | Warning | User-facing text that is not a #Token
SSUI004 | SourceSharp.Ui | Warning | A widget key in a loop that does not use the loop variable
SSUI005 | SourceSharp.Ui | Warning | Mutable static state in a UI assembly
SSUI010 | SourceSharp.Ui | Error | A form field the generator cannot draw
