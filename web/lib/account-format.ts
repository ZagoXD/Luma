export function onlyDigits(value: string) {
  return value.replace(/\D/g, "");
}

export function formatCpf(value: string) {
  const digits = onlyDigits(value).slice(0, 11);
  return digits
    .replace(/^(\d{3})(\d)/, "$1.$2")
    .replace(/^(\d{3})\.(\d{3})(\d)/, "$1.$2.$3")
    .replace(/^(\d{3})\.(\d{3})\.(\d{3})(\d)/, "$1.$2.$3-$4");
}

export function isValidCpf(value: string) {
  const cpf = onlyDigits(value);
  if (cpf.length !== 11 || /^(\d)\1+$/.test(cpf)) return false;

  const firstDigit = calculateCpfDigit(cpf, 9);
  const secondDigit = calculateCpfDigit(cpf, 10);
  return cpf[9] === String(firstDigit) && cpf[10] === String(secondDigit);
}

export function formatBrazilPhone(value: string) {
  let digits = onlyDigits(value).slice(0, 13);
  if (digits.startsWith("55") && digits.length > 11) {
    digits = digits.slice(2);
  }

  digits = digits.slice(0, 11);
  if (digits.length <= 2) return digits;

  const ddd = digits.slice(0, 2);
  const number = digits.slice(2);
  if (number.length <= 4) return `(${ddd}) ${number}`;

  if (number.length <= 8) {
    return `(${ddd}) ${number.slice(0, 4)}-${number.slice(4)}`;
  }

  return `(${ddd}) ${number.slice(0, 5)}-${number.slice(5)}`;
}

export function formatBrazilPhoneDisplay(value: string) {
  const formatted = formatBrazilPhone(value);
  return formatted ? `+55 ${formatted}` : value;
}

export function isValidBrazilPhone(value: string) {
  let digits = onlyDigits(value);
  if (digits.startsWith("55") && digits.length > 11) {
    digits = digits.slice(2);
  }

  if (digits.length !== 10 && digits.length !== 11) return false;

  const ddd = Number(digits.slice(0, 2));
  if (ddd < 11 || ddd > 99) return false;

  const subscriber = digits.slice(2);
  if (/^(\d)\1+$/.test(subscriber)) return false;
  return subscriber.length === 8 || subscriber[0] === "9";
}

function calculateCpfDigit(cpf: string, length: number) {
  let sum = 0;
  for (let i = 0; i < length; i += 1) {
    sum += Number(cpf[i]) * (length + 1 - i);
  }

  const remainder = sum % 11;
  return remainder < 2 ? 0 : 11 - remainder;
}
