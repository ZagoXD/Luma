namespace Luma.Api.Services;

public sealed record NormalizedAccountInput(
    string Email,
    string Cpf,
    string PhoneNumber,
    string FullName,
    string? Error);

public static class AccountInputNormalizer
{
    public static NormalizedAccountInput NormalizeRegistration(
        string email,
        string cpf,
        string phoneNumber,
        string fullName,
        string password)
    {
        var normalizedEmail = email.Trim().ToLowerInvariant();
        var normalizedCpf = OnlyDigits(cpf);
        var normalizedPhone = NormalizeBrazilPhone(phoneNumber);
        var normalizedName = fullName.Trim();

        if (string.IsNullOrWhiteSpace(normalizedEmail) || !normalizedEmail.Contains('@', StringComparison.Ordinal))
        {
            return Invalid(normalizedEmail, normalizedCpf, normalizedPhone, normalizedName, "Informe um e-mail válido.");
        }

        if (!IsValidCpf(normalizedCpf))
        {
            return Invalid(normalizedEmail, normalizedCpf, normalizedPhone, normalizedName, "Informe um CPF válido.");
        }

        if (normalizedPhone is null)
        {
            return Invalid(normalizedEmail, normalizedCpf, string.Empty, normalizedName, "Informe um celular válido com DDD.");
        }

        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            return Invalid(normalizedEmail, normalizedCpf, normalizedPhone, normalizedName, "Informe seu nome completo.");
        }

        if (password.Length < 8)
        {
            return Invalid(normalizedEmail, normalizedCpf, normalizedPhone, normalizedName, "A senha precisa ter pelo menos 8 caracteres.");
        }

        return new NormalizedAccountInput(normalizedEmail, normalizedCpf, normalizedPhone, normalizedName, null);
    }

    public static string? NormalizeBrazilPhone(string value)
    {
        var digits = OnlyDigits(value);
        if (digits.StartsWith("55", StringComparison.Ordinal) && digits.Length is 12 or 13)
        {
            digits = digits[2..];
        }

        if (digits.Length is not (10 or 11))
        {
            return null;
        }

        var ddd = int.Parse(digits[..2]);
        if (ddd is < 11 or > 99)
        {
            return null;
        }

        var subscriber = digits[2..];
        if (subscriber.Length == 9 && subscriber[0] != '9')
        {
            return null;
        }

        if (subscriber.All(digit => digit == subscriber[0]))
        {
            return null;
        }

        return $"+55{digits}";
    }

    public static bool IsValidCpf(string value)
    {
        var cpf = OnlyDigits(value);
        if (cpf.Length != 11 || cpf.All(digit => digit == cpf[0]))
        {
            return false;
        }

        var firstDigit = CalculateCpfDigit(cpf, 9);
        var secondDigit = CalculateCpfDigit(cpf, 10);
        return cpf[9] == CharFromDigit(firstDigit) && cpf[10] == CharFromDigit(secondDigit);
    }

    public static string OnlyDigits(string value)
    {
        return new string(value.Where(char.IsDigit).ToArray());
    }

    private static NormalizedAccountInput Invalid(string email, string cpf, string? phone, string fullName, string error)
    {
        return new NormalizedAccountInput(email, cpf, phone ?? string.Empty, fullName, error);
    }

    private static int CalculateCpfDigit(string cpf, int length)
    {
        var sum = 0;
        for (var i = 0; i < length; i += 1)
        {
            sum += (cpf[i] - '0') * (length + 1 - i);
        }

        var remainder = sum % 11;
        return remainder < 2 ? 0 : 11 - remainder;
    }

    private static char CharFromDigit(int digit)
    {
        return (char)('0' + digit);
    }
}
