"""Render proposal.html to proposal.pdf (A4) and report the page count."""
from pathlib import Path

from playwright.sync_api import sync_playwright
from pypdf import PdfReader

here = Path(__file__).resolve().parent
src = here / "proposal.html"
out = here / "Proposal_Azure_Search_Mihail_Petrov.pdf"

with sync_playwright() as p:
    browser = p.chromium.launch(channel="chrome")
    page = browser.new_page()
    page.goto(src.as_uri())
    page.pdf(path=str(out), format="A4", print_background=True, prefer_css_page_size=True)
    browser.close()

print(out.name, len(PdfReader(str(out)).pages), "pages")
