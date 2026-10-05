// Elenco dei programmi mostrati nel sito.
// Per aggiungerne uno: copia l'exe nella cartella "download" e aggiungi qui una voce.
const PROGRAMMI = [
  {
    nome: "Traduttore riquadro",
    versione: "1.2",
    descrizione:
      "Un riquadro che sposti e ridimensioni sopra qualsiasi cosa sullo schermo: legge il testo che c'è sotto, anche dentro le immagini, e mostra la traduzione al suo posto.",
    funzioni: [
      "Legge testo e immagini con l'OCR integrato di Windows",
      "Lingua originale rilevata automaticamente, oltre 50 lingue di destinazione",
      "Modalità Auto: ritraduce da solo quando il contenuto cambia",
      "Tasti rapidi a scelta per aprire il riquadro e per tradurre al volo il testo selezionato",
      "Icona vicino all'orologio con le impostazioni e l'avvio automatico con Windows",
      "Si aggiorna da solo quando esce una nuova versione",
    ],
    requisiti: "Windows 10 o 11 (64 bit), connessione a internet",
    file: "download/Traduttore.exe",
    sorgente: "https://github.com/adrimas87/programmi-utili/tree/main/TraduttoreRiquadro",
    note:
      "Il testo letto dallo schermo viene inviato a Google Traduttore tramite un servizio gratuito non ufficiale, che potrebbe smettere di funzionare. L'OCR legge le lingue in alfabeto latino; per le altre serve il pacchetto OCR di Windows della lingua.",
  },
];
