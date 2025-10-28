---
title: Welcome to SpocR
description: Code generator for SQL Server stored procedures that creates strongly typed C# classes.
layout: landing
---

## ::hero

title: 'SpocR'
description: 'Code generator for SQL Server stored procedures that creates strongly typed C# classes for inputs, models, and execution.'
headline: 'SQL to C# Code Generation'
links:

- label: 'Get Started'
  to: '/getting-started/installation'
  size: 'lg'
  color: 'black'
  icon: 'i-heroicons-rocket-launch'
- label: 'View on GitHub'
  to: 'https://github.com/nuetzliches/spocr'
  size: 'lg'
  color: 'white'
  variant: 'outline'
  icon: 'i-simple-icons-github'
  target: '\_blank'

---

#title
SpocR: [SQL]{.text-primary} to [C#]{.text-primary} Code Generation

#description
Generate strongly typed C# classes from SQL Server stored procedures with minimal configuration. Reduce boilerplate, increase type safety, and boost development productivity.

::

## ::section

## title: 'Why SpocR?'

::u-container
:::card-group
:::card

---

title: 'Type Safety'
icon: 'i-heroicons-shield-check'

---

Generate strongly typed C# classes that catch errors at compile time instead of runtime.
:::

## :::card

title: 'Zero Boilerplate'
icon: 'i-heroicons-bolt'

---

Eliminate manual mapping code. SpocR handles the tedious data access layer for you.
:::

## :::card

title: 'Fast Integration'
icon: 'i-heroicons-bolt'

---

Integrate into existing .NET solutions within minutes, not hours.
:::

## :::card

title: 'Extensible'
icon: 'i-heroicons-puzzle-piece'

---

Customize naming conventions, output structure, and generation behavior.
:::
:::
::

::

## ::section

## title: 'Quick Start'

::u-container
Get up and running with SpocR in under 5 minutes:

:::code-group

```bash [Install]
dotnet tool install --global SpocR
```

```bash [Initialize]
spocr init --namespace MyCompany.MyProject --connection "Server=.;Database=AppDb;Trusted_Connection=True;" --schemas core,identity
```

```bash [Connect]
spocr pull --connection "Server=.;Database=AppDb;Trusted_Connection=True;"
```

```bash [Generate]
spocr build
```

:::

:::callout
🎉 **That's it!** Your strongly typed C# classes are ready in the `SpocR/` directory by default.
:::

::spacer

::

## <!-- ::section

## title: 'Example Usage'

::u-container
See how clean your data access becomes:

:::code-group

```csharp [Before SpocR]
// Manual, error-prone approach
var command = new SqlCommand("EXEC GetUserById", connection);
command.Parameters.AddWithValue("@UserId", 123);
var reader = await command.ExecuteReaderAsync();

var users = new List<User>();
while (await reader.ReadAsync()) {
    users.Add(new User {
        Id = reader.GetInt32("Id"),
        Name = reader.GetString("Name"),
        Email = reader.IsDBNull("Email") ? null : reader.GetString("Email")
        // ... more manual mapping
    });
}
```

```csharp [With SpocR]
// Generated, type-safe approach
var context = new GeneratedDbContext(connectionString);
var result = await context.GetUserByIdAsync(new GetUserByIdInput {
    UserId = 123
});

// Strongly typed, no manual mapping needed!
foreach (var user in result) {
    Console.WriteLine($"{user.Name} - {user.Email}");
}
```

:::
::

:: -->

## ::section

title: 'Ready to get started?'
links:

- label: 'Installation Guide'
  to: '/getting-started/installation'
  color: 'black'
  size: 'lg'
- label: 'CLI Reference'
  to: '/cli'
  variant: 'outline'
  color: 'black'
  size: 'lg'

---

::u-container
Join developers who've eliminated thousands of lines of boilerplate code with SpocR.
::

::

## ::section

## title: 'Features'

::u-container
:::card-group
:::card

---

title: 'Multiple Output Formats'
icon: 'i-heroicons-document-duplicate'

---

Generate models, data contexts, and extensions with flexible output options.
:::

## :::card

title: 'JSON Support'
icon: 'i-heroicons-code-bracket'

---

Handle complex JSON return types with optional deserialization strategies.
:::

## :::card

title: 'Custom Types'
icon: 'i-heroicons-variable'

---

Support for custom scalar types, table types, and complex parameter structures.
:::

## :::card

title: 'CI/CD Ready'
icon: 'i-heroicons-cog-6-tooth'

---

Integrate seamlessly into build pipelines and automated deployment workflows.
:::
:::
::

::
